using PracticeManager.Core;
using PracticeManager.Persistence;

internal static class ProductionStorageBoundaryTests
{
 internal static void Run(string parent,Action<bool,string> check)
 {
  var root=Directory.CreateDirectory(Path.Combine(parent,"production-layout-fixture")).FullName;
  var profile=Directory.CreateDirectory(Path.Combine(root,"profile")).FullName;
  var projects=Directory.CreateDirectory(Path.Combine(root,"projects")).FullName;
  var backup=Directory.CreateDirectory(Path.Combine(root,"backup")).FullName;
  void Refuse(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  var layout=ProductionStorageBoundary.Review(profile,projects,backup);
  check(layout.ProgramRoot==Path.Combine(profile,"Programs","CRC Practice Manager"),"production code root fixed independently of data");
  check(layout.DatabasePath==Path.Combine(profile,"CRC Practice Manager DATA","practice.db"),"production database root distinct from DEV");
  check(layout.ProjectRoot==projects&&layout.BackupRoot==backup,"project and recovery choices retained");
  check(!Directory.Exists(layout.ProgramRoot)&&!Directory.Exists(layout.DataRoot),"production path review does not create program or data directory");
  Refuse(()=>ProductionStorageBoundary.Review(profile,projects,projects),"same project and recovery root rejected");
  var nested=Directory.CreateDirectory(Path.Combine(projects,"nested")).FullName;
  Refuse(()=>ProductionStorageBoundary.Review(profile,projects,nested),"nested backup root rejected");
  Refuse(()=>ProductionStorageBoundary.Review(profile,profile,backup),"project root cannot contain application or test stores");
  Refuse(()=>ProductionStorageBoundary.Review(profile,projects,profile),"recovery root cannot contain application or test stores");
  var dev=Directory.CreateDirectory(Path.Combine(profile,"CRC Practice Manager DEV")).FullName;
  Refuse(()=>ProductionStorageBoundary.Review(profile,dev,backup),"development root cannot be selected as production projects");
  var acceptance=Directory.CreateDirectory(Path.Combine(profile,"CRC Practice Manager DOCUMENT ACCEPTANCE")).FullName;
  Refuse(()=>ProductionStorageBoundary.Review(profile,projects,acceptance),"acceptance root cannot be selected as production backup");
  var programParent=Path.GetDirectoryName(layout.ProgramRoot)!;
  File.WriteAllText(programParent,"synthetic collision");
  Refuse(()=>ProductionStorageBoundary.Review(profile,projects,backup),"unexpected file at program parent rejected");
  check(File.ReadAllText(programParent)=="synthetic collision","program parent collision remains unchanged");
  File.Delete(programParent);
  if(OperatingSystem.IsWindows())
  {
   Refuse(()=>layout.VerifyExistingPrivateDataLayout(),"missing production store cannot be silently initialized");
   WorkspaceConfidentiality.EnsureWorkspaceDirectory(layout.DataRoot);
   Refuse(()=>layout.VerifyExistingPrivateDataLayout(),"missing identity marker and database rejected");
   File.WriteAllText(layout.IdentityMarkerPath,"synthetic layout only");
   Refuse(()=>layout.VerifyExistingPrivateDataLayout(),"marker alone cannot qualify missing database");
   File.WriteAllText(layout.DatabasePath,"synthetic bytes not a database");
   layout.VerifyExistingPrivateDataLayout();
   check(File.ReadAllText(layout.DatabasePath)=="synthetic bytes not a database","layout verification does not open or modify database content");
   Directory.CreateDirectory(layout.DatabasePath+"-wal");
   Refuse(()=>layout.VerifyExistingPrivateDataLayout(),"unexpected sidecar directory rejected");
   var profile2=Directory.CreateDirectory(Path.Combine(root,"identity-profile")).FullName;
   var candidate=ProductionStorageBoundary.Review(profile2,projects,backup);
   var id=ProductionWorkspaceIdentityCandidate.InitializeNewFixture(candidate);
   check(Guid.TryParseExact(id,"N",out var parsed)&&parsed!=Guid.Empty,"fresh production candidate assigns unique identity");
   check(ProductionWorkspaceIdentityCandidate.VerifyExisting(candidate)==id,"marker and database identity match read-only");
   var original=File.ReadAllBytes(candidate.DatabasePath);
   var marker=File.ReadAllText(candidate.IdentityMarkerPath);
   Refuse(()=>ProductionWorkspaceIdentityCandidate.InitializeNewFixture(candidate),"existing production location cannot be initialized again");
   check(original.SequenceEqual(File.ReadAllBytes(candidate.DatabasePath)),"repeated initialization preserves original database bytes");
   try{_=new LocalPractice(candidate.DatabasePath);throw new Exception("FAIL: production candidate opened by DEV");}
   catch(InvalidOperationException){check(original.SequenceEqual(File.ReadAllBytes(candidate.DatabasePath)),"development opener refuses production candidate without database mutation");}
   File.WriteAllText(candidate.IdentityMarkerPath,"CRC-PRACTICE-PRODUCTION-V1:"+Guid.NewGuid().ToString("N"));
   Refuse(()=>ProductionWorkspaceIdentityCandidate.VerifyExisting(candidate),"different marker identity refused");
   File.WriteAllText(candidate.IdentityMarkerPath,marker);
   File.Delete(candidate.IdentityMarkerPath);
   Refuse(()=>ProductionWorkspaceIdentityCandidate.VerifyExisting(candidate),"interrupted candidate with missing marker refused");
   Refuse(()=>ProductionWorkspaceIdentityCandidate.InitializeNewFixture(candidate),"interrupted candidate cannot be initialized over");
   File.WriteAllText(candidate.IdentityMarkerPath,marker);
   check(ProductionWorkspaceIdentityCandidate.VerifyExisting(candidate)==id,"restored synthetic marker matches unchanged database");
   File.Delete(candidate.DatabasePath);
   Refuse(()=>ProductionWorkspaceIdentityCandidate.VerifyExisting(candidate),"missing candidate database not reopened");
   check(!File.Exists(candidate.DatabasePath),"missing candidate database remains absent");
   Refuse(()=>ProductionWorkspaceIdentityCandidate.InitializeNewFixture(candidate),"existing marker cannot trigger database replacement");
  }
 }
}
