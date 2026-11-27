using Microsoft.Data.Sqlite;
using PracticeManager.Core;
using PracticeManager.Persistence;
using System.Security.Cryptography;
internal static class TransmittalRegisterStoreTests
{
 public static void Run(Action<bool,string> check)
 {
  using var db=new SqliteConnection("Data Source=:memory:;Foreign Keys=True;Pooling=False");db.Open();
  void Sql(string sql){using var q=db.CreateCommand();q.CommandText=sql;q.ExecuteNonQuery();}
  Sql(@"CREATE TABLE projects(id INTEGER PRIMARY KEY);CREATE TABLE users(id INTEGER PRIMARY KEY);
  CREATE TABLE audit_events(id INTEGER PRIMARY KEY,actor_id INTEGER REFERENCES users(id),action TEXT,entity TEXT,entity_id INTEGER,occurred_at TEXT);
  CREATE TABLE project_documents(id INTEGER PRIMARY KEY,project_id INTEGER REFERENCES projects(id),supersedes_id INTEGER REFERENCES project_documents(id),classification TEXT,category TEXT,file_name TEXT,file_hash TEXT,file_bytes BLOB);
  INSERT INTO projects VALUES(1),(2);INSERT INTO users VALUES(1)");
  using(var stream=typeof(TransmittalRegisterStoreTests).Assembly.GetManifestResourceStream("SyntheticDocumentRegister.sql")!)
  using(var reader=new StreamReader(stream))Sql(reader.ReadToEnd());
  var pdf="%PDF-1.7\nSynthetic\n%%EOF"u8.ToArray();var hash=Convert.ToHexString(SHA256.HashData(pdf));
  void Doc(long id,long? prior)
  {
   using var q=db.CreateCommand();q.CommandText="INSERT INTO project_documents VALUES($i,1,$prev,'Production','Drawings','drawing.pdf',$h,$b)";
   q.Parameters.AddWithValue("$i",id);q.Parameters.AddWithValue("$prev",(object?)prior??DBNull.Value);q.Parameters.AddWithValue("$h",hash);q.Parameters.AddWithValue("$b",pdf);q.ExecuteNonQuery();
  }
  Doc(1,null);bool permitted=true,eligible=true;
  var store=new TransmittalRegisterStore(db,(_,p,_)=>permitted&&p==1?1:throw new UnauthorizedAccessException(),(_,_)=>{if(!eligible)throw new ArgumentException("Synthetic QA gate.");});
  TransmittalIntent Intent(long id,string purpose)=>DocumentTransmittalReview.Prepare(1,"Synthetic recipient",purpose,new[]{new ReviewedIssueDocument(id,hash,"drawing.pdf","Drawings",pdf.Length)});
  void Invalid(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  var intent=Intent(1,"Review");var op=Guid.NewGuid();
  var prepared=store.Prepare(op,intent,"Synthetic review evidence","Reviewed",true);
  check(!prepared.Replayed&&prepared.Record.History.Single().Event.State=="Prepared","native transmittal atomically seals manifest and preparation audit");
  check(store.Prepare(op,intent,"Synthetic review evidence","Reviewed",true).Replayed,"exact preparation replay returns original");
  Invalid(()=>store.Prepare(op,intent,"Changed","Reviewed",true),"changed preparation replay refused");
  var id=prepared.Record.Id;var prior=prepared.Record.History.Single().Event.Hash;
  Invalid(()=>store.Append(1,id,Guid.NewGuid(),prior,"DeliveryRecorded","Synthetic evidence","Review",true),"native delivery cannot precede issue");
  eligible=false;
  Invalid(()=>store.Append(1,id,Guid.NewGuid(),prior,"IssueRecorded","Synthetic evidence","Review",true),"native issue requires current eligibility callback");
  eligible=true;
  var issueOp=Guid.NewGuid();var issue=store.Append(1,id,issueOp,prior,"IssueRecorded","Synthetic issue evidence","Reviewed",true);
  Doc(2,1);
  var delivery=store.Append(1,id,Guid.NewGuid(),issue.Entry.Event.Hash,"DeliveryRecorded","Synthetic receipt","Reviewed",true);
  check(delivery.Entry.Event.State=="DeliveryRecorded"&&store.Read(1,id).History.Count==3,"historical delivery retains originally issued version");
  check(store.Append(1,id,issueOp,prior,"IssueRecorded","Synthetic issue evidence","Reviewed",true).Replayed,"exact issue replay survives supersession");
  Invalid(()=>store.Prepare(Guid.NewGuid(),intent,"Synthetic review","Reviewed",true),"new preparation rejects superseded document");
  var next=store.Prepare(Guid.NewGuid(),Intent(2,"New review"),"Synthetic review","Reviewed",true).Record;
  Doc(3,2);
  Invalid(()=>store.Append(1,next.Id,Guid.NewGuid(),next.History[0].Event.Hash,"IssueRecorded","Synthetic issue","Reviewed",true),"issue rejects version changed after preparation");
  permitted=false;
  try{store.Read(1,id);throw new Exception("FAIL: transmittal access");}catch(UnauthorizedAccessException){check(true,"native transmittal access rechecked");}
  permitted=true;
  Sql("DROP TRIGGER project_transmittal_events_no_update;UPDATE project_transmittal_events SET note='Tampered' WHERE sequence=1");
  try{store.Read(1,id);throw new Exception("FAIL: transmittal tamper");}catch(ArgumentException){check(true,"native transmittal verifies event chain");}
 }
}
