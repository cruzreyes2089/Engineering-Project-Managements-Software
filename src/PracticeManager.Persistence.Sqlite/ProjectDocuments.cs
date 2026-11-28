using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;
public sealed partial class LocalPractice
{
 private const int MaxProjectDocumentBytes=20*1024*1024;
 public static IReadOnlyList<string> DocumentCategories{get;}=Array.AsReadOnly(new[]{"Correspondence","Drawings","Calculations","Reports","Contracts","Other"});
 private static readonly string[] DocumentExtensions=[".pdf",".dwg",".dxf",".rvt",".docx",".xlsx",".txt"];
 private static string SafeDocumentName(string name)
 {
  var n=Validation.Text(name,180,"File name",true);
  if(n!=name||n.Any(ch=>char.IsControl(ch)||ch is '/' or '\\' or ':' or '<' or '>' or '"' or '|' or '?' or '*')||n.EndsWith('.')||n.StartsWith('.'))throw new ArgumentException("Use a plain document file name without a path.");
  if(LocalStoragePath.IsReservedWindowsName(n))throw new ArgumentException("Reserved file name.");
  if(!DocumentExtensions.Contains(Path.GetExtension(n),StringComparer.OrdinalIgnoreCase))throw new ArgumentException("Unsupported document type. Executables, macros and archives are not accepted.");
  return n;
 }
 private long RequireDocuments(SqliteConnection c,SqliteTransaction t,bool record)
 {
  var actor=Require(c,t,"Documents.View");RequireProjectView(c,t);if(record)Require(c,t,"Documents.Record");return actor;
 }
 private bool CanInTransaction(SqliteConnection c,SqliteTransaction t,string permission)
 {
  try{Require(c,t,permission);return true;}catch(UnauthorizedAccessException){return false;}
 }
 private const string DocumentProjection=@"SELECT d.id,d.project_id,d.supersedes_id,d.classification,d.category,d.file_name,d.file_hash,length(d.file_bytes),d.recorded_utc,NOT EXISTS(SELECT 1 FROM project_documents n WHERE n.supersedes_id=d.id) FROM project_documents d";
 private static ProjectDocument DocumentRow(SqliteDataReader r)=>new(r.GetInt64(0),r.GetInt64(1),r.IsDBNull(2)?null:r.GetInt64(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetInt64(7),r.GetString(8),r.GetInt64(9)==1);
 private static ProjectDocument? FindDocument(SqliteConnection c,SqliteTransaction t,long project,long id)
 {
  using var q=Command(c,t,DocumentProjection+" WHERE d.project_id=$p AND d.id=$i",("$p",project),("$i",id));using var r=q.ExecuteReader();return r.Read()?DocumentRow(r):null;
 }
 public IReadOnlyList<ProjectDocument> ReadProjectDocuments(long project,bool currentOnly=true,string search="",string category="All")
 {
  using var c=Open();using var t=c.BeginTransaction();RequireDocuments(c,t,false);ProjectRow(c,t,project);var restricted=CanInTransaction(c,t,"Documents.Restricted");
  if(category!="All"&&!DocumentCategories.Contains(category))throw new ArgumentException("Select a valid document category.");
  var term=Validation.Text(search,150,"Document search");
  using var q=Command(c,t,DocumentProjection+" WHERE d.project_id=$p AND ($r=1 OR d.classification='Production') AND ($current=0 OR NOT EXISTS(SELECT 1 FROM project_documents n WHERE n.supersedes_id=d.id)) AND ($category='All' OR d.category=$category) AND ($search='' OR instr(lower(d.file_name),lower($search))>0) ORDER BY d.id DESC LIMIT 501",("$p",project),("$r",restricted?1:0),("$current",currentOnly?1:0),("$category",category),("$search",term));using var r=q.ExecuteReader();var rows=new List<ProjectDocument>();while(r.Read()){if(rows.Count==500)throw new ArgumentException("Document register exceeds 500 rows; show current versions or refine the search.");rows.Add(DocumentRow(r));}return rows.AsReadOnly();
 }
 public bool CanRegisterProjectDocuments(long project)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:true);
  try{RequireDocuments(c,t,true);ProjectRow(c,t,project);return true;}catch(UnauthorizedAccessException){return false;}
 }
 public long RegisterProjectDocument(long project,Guid operation,long? supersedes,string classification,string category,string name,byte[] bytes)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireDocuments(c,t,true);ProjectRow(c,t,project);
  if(classification is not ("Production" or "Restricted")||!DocumentCategories.Contains(category)||operation==Guid.Empty)throw new ArgumentException("Select valid document metadata.");
  if(classification=="Restricted")Require(c,t,"Documents.Restricted");
  var file=SafeDocumentName(name);if(bytes==null||bytes.Length is <1 or >MaxProjectDocumentBytes)throw new ArgumentException("Document size must be 1 byte to 20 MiB.");
  var copy=bytes.ToArray();var hash=Convert.ToHexString(SHA256.HashData(copy));var key=operation.ToString("D");
  using(var q=Command(c,t,"SELECT id,project_id,supersedes_id,classification,category,file_name,file_hash FROM project_documents WHERE operation_key=$k",("$k",key)))
  using(var r=q.ExecuteReader()){
   if(r.Read()){
    if(r.GetInt64(1)!=project||(r.IsDBNull(2)?(long?)null:r.GetInt64(2))!=supersedes||r.GetString(3)!=classification||r.GetString(4)!=category||r.GetString(5)!=file||r.GetString(6)!=hash)throw new ArgumentException("Document operation conflict.");
    return r.GetInt64(0);
   }
  }
  if(supersedes.HasValue){var prior=FindDocument(c,t,project,supersedes.Value);if(prior==null||!prior.IsCurrent||prior.Classification!=classification||prior.Category!=category)throw new ArgumentException("Current matching document version required.");}
  Exec(c,t,"INSERT INTO project_documents(project_id,operation_key,supersedes_id,classification,category,file_name,file_bytes,file_hash,recorded_by,recorded_utc) VALUES($p,$k,$s,$c,$g,$f,$b,$h,$a,$u)",("$p",project),("$k",key),("$s",supersedes),("$c",classification),("$g",category),("$f",file),("$b",copy),("$h",hash),("$a",actor),("$u",UtcNow()));var id=Last(c,t);Audit(c,t,actor,"RegisterDocument","ProjectDocument",id);t.Commit();return id;
 }
 public ProjectDocumentContent RetrieveProjectDocument(long project,long document,string expectedHash)
 {
  using var c=Open();using var t=c.BeginTransaction(deferred:false);var actor=RequireDocuments(c,t,false);ProjectRow(c,t,project);
  var d=FindDocument(c,t,project,document);
  if(d==null||d.Classification=="Restricted"&&!CanInTransaction(c,t,"Documents.Restricted"))throw new UnauthorizedAccessException("Document unavailable.");
  if(d.Hash!=expectedHash)throw new ArgumentException("Document identity changed.");
  var data=Scalar(c,t,"SELECT file_bytes FROM project_documents WHERE id=$i AND project_id=$p",("$i",document),("$p",project));
  if(data is not byte[] bytes||bytes.LongLength!=d.Bytes||bytes.Length>MaxProjectDocumentBytes||Convert.ToHexString(SHA256.HashData(bytes))!=d.Hash)throw new InvalidOperationException("Document integrity verification failed.");
  Audit(c,t,actor,"RetrieveDocument","ProjectDocument",document);t.Commit();return new(d,bytes);
 }
}
