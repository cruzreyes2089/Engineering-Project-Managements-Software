using PracticeManager.Core;

// Exercises the configurable nested plan in disposable storage, never a saved firm root.
internal static class SyntheticNestedFolderTests
{
 public static void Run(string ownedRoot,Action<bool,string> check)
 {
  var root=Path.Combine(ownedRoot,"nested-folders-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(root);
  var production=Directory.CreateDirectory(Path.Combine(root,"SyntheticProduction")).FullName;
  var restricted=Directory.CreateDirectory(Path.Combine(root,"SyntheticRestricted")).FullName;
  using(var f=new FileStream(Path.Combine(root,SyntheticFolderOperationFence.FileName),FileMode.CreateNew,FileAccess.Write,FileShare.None))f.Flush(true);
  var layout=ProjectFolderLayout.Named(101,new[]{"Design","Design/Drawings","Design/Drawings/Details","Design/Drawings/Details/Issued","Reports"});
  var folders=new SyntheticFolderProvisioning(root,61,"PRJ-000061",layout);
  var journal=new SyntheticFolderJournal(Path.Combine(root,"synthetic-journal.db"),folders,61);
  var pp=Path.Combine(production,"PRJ-000061");var rp=Path.Combine(restricted,"PRJ-000061");
  void Reject<T>(Action action,string label) where T:Exception
  {try{action();}catch(T){check(true,label);return;}throw new Exception("FAIL: "+label);}
  var initial=folders.Preview(true,true);
  check(initial.Steps.Count==12&&initial.Steps.All(x=>!x.Exists)&&!Directory.Exists(pp)&&!Directory.Exists(rp),
   "nested folder preview nonmutating with distinct restricted root");
  var original=ProjectFolderPlanning.Build(61,"PRJ-000061",true,true,layout);
  check(initial.PlanFingerprint==original.Fingerprint,"nested physical candidate uses exact logical template plan");
  // Pre-existing nested file collision: preserve the entire fixture, no journal intent.
  Directory.CreateDirectory(Path.Combine(pp,"Design","Drawings"));
  var collision=Path.Combine(pp,"Design","Drawings","Details");File.WriteAllText(collision,"Synthetic retain");
  Reject<IOException>(()=>folders.Preview(true,true),"deep file collision denied before intent");
  check(File.ReadAllText(collision)=="Synthetic retain"&&journal.Pending()==null&&!Directory.Exists(rp),"deep collision leaves unrelated roots untouched");
  File.Delete(collision);
  var before=folders.Preview(true,true);
  Reject<IOException>(()=>journal.ExecuteReviewed(true,true,before.ReviewFingerprint,true,afterCreate:n=>{if(n==2)throw new IOException("Synthetic nested interruption");}),"nested journal interruption");
  var pending=journal.Pending()??throw new Exception("Pending nested intent missing.");
  var current=folders.Preview(true,true);
  var changed=new SyntheticFolderProvisioning(root,61,"PRJ-000061",ProjectFolderLayout.Named(102,new[]{"Other"}));
  var alternate=new SyntheticFolderJournal(Path.Combine(root,"synthetic-journal.db"),changed,61);
  Reject<ArgumentException>(()=>alternate.ExecuteReviewed(true,true,changed.Preview(true,true).ReviewFingerprint,true,pending),"nested repair cannot replace template revision");
  var repaired=journal.ExecuteReviewed(true,true,current.ReviewFingerprint,true,pending);
  check(journal.State(pending)=="Superseded"&&journal.State(repaired)=="Confirmed"&&
   Directory.Exists(Path.Combine(pp,"Design","Drawings","Details","Issued"))&&Directory.GetDirectories(rp).Length==4,
   "nested interrupted plan repaired without changing template");
  var replay=folders.Preview(true,true);
  check(folders.Apply(true,true,replay.ReviewFingerprint,true)==(0,12),"nested replay creates no directories");
  if(OperatingSystem.IsWindows())
  {
   var leaf=Path.Combine(pp,"Design","Drawings","Details","Issued");
   var moved=leaf+"-original";
   Reject<IOException>(()=>journal.ExecuteReviewed(true,true,replay.ReviewFingerprint,true,beforeOutcome:()=>{
    Directory.Move(leaf,moved);Directory.CreateDirectory(leaf);
   }),"same-path replacement before journal outcome leaves uncertainty");
   var uncertain=journal.Pending()??throw new Exception("Missing uncertain nested operation.");
   check(journal.State(uncertain)=="Uncertain","nested replaced directory not automatically confirmed");
   // Restore only our test-created original directory before review and repair.
   Directory.Delete(leaf,false);Directory.Move(moved,leaf);
   var reviewed=folders.Preview(true,true);
   check(journal.State(journal.ExecuteReviewed(true,true,reviewed.ReviewFingerprint,true,uncertain))=="Confirmed",
    "nested restored identity repaired with explicit review");
  }
  var linked=new SyntheticFolderProvisioning(root,62,"PRJ-000062",layout);
  var parent=Directory.CreateDirectory(Path.Combine(production,"PRJ-000062")).FullName;
  var unrelated=Directory.CreateDirectory(Path.Combine(root,"Unrelated")).FullName;
  var link=Path.Combine(parent,"Design");bool made=false;
  try
  {
   try{Directory.CreateSymbolicLink(link,unrelated);made=true;}
   catch(UnauthorizedAccessException){}catch(IOException){}catch(PlatformNotSupportedException){}
   if(made)
   {
    Reject<IOException>(()=>linked.Preview(false,false),"redirected nested ancestor denied before descendant inspection");
    check(!Directory.EnumerateFileSystemEntries(unrelated).Any(),"nested redirected target untouched");
   }
   else Console.WriteLine("NOT QUALIFIED: nested directory symlink unavailable.");
  }
  finally{if(made)Directory.Delete(link,false);}
 }
}
