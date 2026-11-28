using Microsoft.Data.Sqlite;
namespace PracticeManager.Persistence;
public sealed record DocumentManagementReport(string ProjectNumber,int ProductionVersions,int CurrentProductionVersions,int CurrentProductionPdfs,
 bool HasRegisteredFolder,bool FolderUsesCurrentConfiguration,int Preparations,int Issues,int Deliveries,int Withdrawals,
 int EverIssued,int EverDelivered,int SupersededPreparations,int OpenQualityItems);
public sealed partial class LocalPractice
{
 public DocumentManagementReport ReadAcceptanceDocumentManagementReport(long project)
 {
  RequireDocumentAcceptance();using var c=Open();using var t=c.BeginTransaction(deferred:true);
  RequireDocuments(c,t,false);RequireAdministration(c,t);Require(c,t,"Reports.Operations");RequireDocumentRegisterCandidate(c,t);
  var p=ProjectRow(c,t,project);
  var config=CandidateFolderConfiguration(c).ReadCore(t).LastOrDefault();
  var folder=CandidateProjectFolders(c).ReadCore(t,project).LastOrDefault();
  var records=CandidateTransmittalStore(c).ListCore(t,project);
  int Count(string sql)=>checked((int)Convert.ToInt64(Scalar(c,t,sql,("$p",project))));
  const string current=" AND NOT EXISTS(SELECT 1 FROM project_documents n WHERE n.supersedes_id=d.id)";
  const string production="SELECT count(*) FROM project_documents d WHERE d.project_id=$p AND d.classification='Production'";
  int State(string state)=>records.Count(x=>x.History.Last().Event.State==state);
  var stale=records.Count(x=>x.History.Last().Event.State=="Prepared"&&x.Intent.Documents.Any(d=>FindDocument(c,t,project,d.Id)?.IsCurrent!=true));
  return new(p.Number,Count(production),Count(production+current),Count(production+current+" AND lower(substr(d.file_name,-4))='.pdf'"),
   folder?.State=="Registered",folder?.State=="Registered"&&folder.ConfigurationHash==config?.Hash,
   State("Prepared"),State("IssueRecorded"),State("DeliveryRecorded"),State("Withdrawn"),
   records.Count(x=>x.History.Any(e=>e.Event.State=="IssueRecorded")),records.Count(x=>x.History.Any(e=>e.Event.State=="DeliveryRecorded")),stale,
   Count("SELECT count(*) FROM project_quality_items i WHERE i.project_id=$p AND NOT EXISTS(SELECT 1 FROM project_quality_closures x WHERE x.item_id=i.id)"));
 }
}
