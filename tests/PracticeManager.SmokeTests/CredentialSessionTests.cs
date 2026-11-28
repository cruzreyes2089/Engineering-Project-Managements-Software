using System.Diagnostics;
using System.Reflection;
using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class CredentialSessionTests
{
 public static void Run(string path,string password,Action<bool,string> check)
 {
  var hash=PasswordCodec.Hash(password);var other=PasswordCodec.Hash(password);
  check(hash!=other&&PasswordCodec.Verify(password,hash)&&PasswordCodec.Verify(password,other),"password salts independent and existing format verifies");
  check(!PasswordCodec.Verify("different password",hash),"wrong password rejected");
  foreach(var malformed in new[]{new string('a',100000),hash+" ",hash.Replace("600000","600001"),hash.Replace("pbkdf2-sha256","PBKDF2-sha256"),hash[..^1]+"!",hash.Replace('$','|')})
   check(!PasswordCodec.Verify(password,malformed),"malformed bounded password encoding denied");
  check(!PasswordCodec.Verify(null!,hash)&&!PasswordCodec.Verify(password,null!)&&!PasswordCodec.Verify(new string('x',257),hash),"null and oversized password inputs denied");
  try{PasswordCodec.Hash(null!);throw new Exception("Null password accepted");}catch(ArgumentException){check(true,"null password creation rejected");}
  var s=new LocalPractice(path);s.Bootstrap("Synthetic session firm","admin",password,"SS");
  void Denied(Action a,string label){try{a();}catch(UnauthorizedAccessException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  Denied(()=>s.FirmName(),"firm identity denied before login");
  check(s.Login("admin",password)&&s.FirmName()=="Synthetic session firm","firm identity after authentication");
  // Synthetic instance only: inject stale clock fields without changing the OS clock.
  var elapsed=typeof(LocalPractice).GetField("sessionStarted",BindingFlags.Instance|BindingFlags.NonPublic)!;
  elapsed.SetValue(s,Stopwatch.GetTimestamp()-checked(Stopwatch.Frequency*9L*3600));
  check(!s.Can("Clients.View"),"monotonic session expiry denies domain permission");
  Denied(()=>s.FirmName(),"monotonic session expiry denies firm identity");
  check(s.Login("admin",password)&&s.Can("Clients.View"),"fresh login restores valid session");
  var wall=typeof(LocalPractice).GetField("expires",BindingFlags.Instance|BindingFlags.NonPublic)!;
  wall.SetValue(s,DateTimeOffset.UtcNow.AddSeconds(-1));
  check(!s.Can("Clients.View"),"wall clock expiry denies domain permission");
  Denied(()=>s.FirmName(),"wall clock expiry denies firm identity");
  check(s.Login("admin",password),"login after wall expiry");
  var idle=typeof(LocalPractice).GetField("sessionLastActivity",BindingFlags.Instance|BindingFlags.NonPublic)!;
  idle.SetValue(s,Stopwatch.GetTimestamp()-checked(Stopwatch.Frequency*14L*60));
  var previous=(long)idle.GetValue(s)!;
  var stamp=s.ReadSessionAccessStamp();
  check((long)idle.GetValue(s)! == previous,"session guard reads do not renew inactivity");
  s.RegisterSessionActivity();
  check((long)idle.GetValue(s)! > previous&&s.ReadSessionAccessStamp()==stamp,"explicit activity renews idle clock without changing access stamp");
  idle.SetValue(s,Stopwatch.GetTimestamp()-checked(Stopwatch.Frequency*16L*60));
  check(!s.Can("Clients.View"),"idle session denies domain permission");
  Denied(()=>s.RegisterSessionActivity(),"expired inactivity cannot be revived by input");
  Denied(()=>s.ReadSessionAccessStamp(),"idle session denies access stamp");
  check(s.Login("admin",password)&&s.ReadSessionAccessStamp()==stamp,"fresh authentication restores session with same grants");
  using var gate=new ManualResetEventSlim(false);
  AccountingProbeTests.WithServer(200,"{}",async (url,observed)=>{
   // The synthetic server waits until the original session has been replaced.
   try{
    var probe=s.TestAccountingConnectionAsync(url,"synthetic-session-key");
    await observed.WaitAsync(TimeSpan.FromSeconds(5));
    s.Logout();check(s.Login("admin",password),"same actor can authenticate a new session");
    gate.Set();
    try{await probe;throw new Exception("FAIL: earlier-session accounting result accepted");}
    catch(UnauthorizedAccessException){check(true,"earlier-session accounting result denied after same-actor login");}
   }finally{gate.Set();}
  },responseFor:_=>gate.Wait(TimeSpan.FromSeconds(10))?"{}":throw new TimeoutException("Synthetic session gate timed out.")).GetAwaiter().GetResult();
  s.Logout();
  Denied(()=>s.FirmName(),"logout denies firm identity");
 }
}
