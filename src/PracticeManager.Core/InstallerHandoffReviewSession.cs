using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
namespace PracticeManager.Core;

// Qualification-only current-user channel. The parent supplies the exact
// child Process it started; OS PID is checked while its process handle is
// retained. A nonce response demonstrates channel liveness, NOT code signing,
// same-user adversary protection, or production authorization. No launch here.
internal sealed class InstallerHandoffReviewSession : IDisposable
{
 readonly InstallerExtractionReservation reservation;
 readonly InstallerReleaseEvidence.Evidence release;
 readonly InstallerExtractionEvidence.Witness witness;
 readonly DateTimeOffset initialTime;
 readonly Stopwatch elapsed=Stopwatch.StartNew();
 readonly NamedPipeServerStream pipe;
 int attempted;
 internal string PipeName{get;}
 internal sealed record Result(string Commit,string Inventory,int PeerPid)
 {internal bool MayExecute=>false;}
 static void Require(bool ok)
 {if(!ok)throw new ArgumentException("Installer handoff channel requires review.");}
 internal InstallerHandoffReviewSession(InstallerExtractionReservation reservation,InstallerReleaseEvidence.Evidence release,DateTimeOffset now)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
  this.reservation=reservation;this.release=release;initialTime=now;
  witness=reservation.ReviewForHandoff(release,now);
  PipeName="CRC-synthetic-handoff-"+Guid.NewGuid().ToString("N");
  pipe=new NamedPipeServerStream(PipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
 }
 [DllImport("kernel32.dll",ExactSpelling=true,SetLastError=true)]
 [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
 [return:MarshalAs(UnmanagedType.Bool)]
 static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out uint clientProcessId);
 internal async Task<Result> ReviewAsync(Process expectedPeer)
 {
  Require(Interlocked.Exchange(ref attempted,1)==0);
  try
  {
   Require(OperatingSystem.IsWindows()&&!expectedPeer.HasExited);
   // Keep the process object/handle alive until the exchange finishes.
   _=expectedPeer.SafeHandle;
   var pid=expectedPeer.Id;
   using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));
   await pipe.WaitForConnectionAsync(deadline.Token);
   Require(GetNamedPipeClientProcessId(pipe.SafePipeHandle,out uint observed)&&observed==(uint)pid&&!expectedPeer.HasExited);
   Require(elapsed.Elapsed<TimeSpan.FromSeconds(15)&&reservation.ReviewForHandoff(release,initialTime+elapsed.Elapsed)==witness);
   var challenge=new byte[64];
   RandomNumberGenerator.Fill(challenge.AsSpan(0,32));
   SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{release,witness}),challenge.AsSpan(32,32));
   await pipe.WriteAsync(challenge,deadline.Token);
   var response=new byte[32];
   await pipe.ReadExactlyAsync(response,deadline.Token);
   Require(CryptographicOperations.FixedTimeEquals(response,SHA256.HashData(challenge))&&!expectedPeer.HasExited);
   Require(GetNamedPipeClientProcessId(pipe.SafePipeHandle,out observed)&&observed==(uint)pid);
   Require(elapsed.Elapsed<TimeSpan.FromSeconds(15)&&reservation.ReviewForHandoff(release,initialTime+elapsed.Elapsed)==witness);
   await pipe.WriteAsync(new byte[]{1},deadline.Token);
   return new(release.Commit,witness.Inventory,pid);
  }
  finally{pipe.Dispose();GC.KeepAlive(expectedPeer);}
 }
 public void Dispose(){Interlocked.Exchange(ref attempted,1);pipe.Dispose();}
}
