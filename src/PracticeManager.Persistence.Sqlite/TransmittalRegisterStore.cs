using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PracticeManager.Core;
namespace PracticeManager.Persistence;

internal sealed record StoredTransmittalEvent(int Sequence,string Operation,TransmittalEvent Event,long AuditId,string MetadataHash);
internal sealed record StoredTransmittal(long Id,string Operation,TransmittalIntent Intent,IReadOnlyList<StoredTransmittalEvent> History);
// Internal inactive candidate. Callbacks must eventually implement real session,
// project and issue eligibility checks within the caller-owned transaction.
internal sealed class TransmittalRegisterStore(SqliteConnection db,Func<SqliteTransaction,long,string,long> authorize,Action<SqliteTransaction,long> issueGate)
{
 static string Key(Guid id)=>id==Guid.Empty?throw new ArgumentException("Operation required."):id.ToString("N");
 static bool ValidKey(string key)=>Guid.TryParseExact(key,"N",out var id)&&id!=Guid.Empty&&id.ToString("N")==key;
 static string Metadata(long id,string intentOperation,StoredTransmittalEvent row)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{
  Format="CRC-TransmittalMetadata-v1",Id=id,IntentOperation=intentOperation,row.Sequence,row.Operation,EventHash=row.Event.Hash,row.AuditId}))).ToLowerInvariant();
 SqliteCommand Q(SqliteTransaction t,string sql,params (string,object?)[] args)
 {
  var q=db.CreateCommand();q.Transaction=t;q.CommandText=sql;foreach(var (key,value) in args)q.Parameters.AddWithValue(key,value??DBNull.Value);return q;
 }
 long Count(SqliteTransaction t,string sql,params (string,object?)[] args){using var q=Q(t,sql,args);return Convert.ToInt64(q.ExecuteScalar());}
 void Exec(SqliteTransaction t,string sql,params (string,object?)[] args){using var q=Q(t,sql,args);q.ExecuteNonQuery();}
 long Actor(SqliteTransaction t,long project,string action)
 {
  var actor=authorize(t,project,action);return actor>0?actor:throw new UnauthorizedAccessException();
 }
 ReviewedIssueDocument Document(SqliteTransaction t,long project,long id,string expected,bool current)
 {
  using var q=Q(t,@"SELECT d.file_name,d.category,d.file_hash,d.classification,length(d.file_bytes),
   CASE WHEN length(d.file_bytes) BETWEEN 5 AND 20971520 THEN d.file_bytes ELSE NULL END,
   NOT EXISTS(SELECT 1 FROM project_documents n WHERE n.supersedes_id=d.id)
   FROM project_documents d WHERE d.project_id=$p AND d.id=$i",("$p",project),("$i",id));
  using var r=q.ExecuteReader();
  if(!r.Read()||r.GetString(3)!="Production")throw new UnauthorizedAccessException("Document unavailable.");
  if(r.GetString(2)!=expected||current&&r.GetInt64(6)!=1)throw new ArgumentException("Document version changed.");
  if(r.IsDBNull(5)||r.GetValue(5) is not byte[] bytes||bytes.LongLength!=r.GetInt64(4)||!bytes.AsSpan(0,5).SequenceEqual("%PDF-"u8)||
     Convert.ToHexString(SHA256.HashData(bytes))!=expected)throw new InvalidOperationException("Document bytes failed verification.");
  return new(id,expected,r.GetString(0),r.GetString(1),bytes.LongLength);
 }
 StoredTransmittal ReadCore(SqliteTransaction t,long project,long id)
 {
  string operation,hash,recipient,purpose;int count;
  using(var q=Q(t,"SELECT operation_key,intent_hash,recipient,purpose,expected_documents FROM project_transmittal_intents WHERE id=$i AND project_id=$p",("$i",id),("$p",project)))
  using(var r=q.ExecuteReader())
  {
   if(!r.Read())throw new UnauthorizedAccessException("Transmittal unavailable.");
   operation=r.GetString(0);hash=r.GetString(1);recipient=r.GetString(2);purpose=r.GetString(3);count=r.GetInt32(4);
  }
  if(!ValidKey(operation)||count is <1 or >50)throw new InvalidOperationException("Invalid transmittal identity.");
  var selections=new List<(long Id,string Hash)>();
  using(var q=Q(t,"SELECT document_id,file_hash FROM project_transmittal_documents WHERE intent_id=$i ORDER BY document_id LIMIT 51",("$i",id)))
  using(var r=q.ExecuteReader())while(r.Read())selections.Add((r.GetInt64(0),r.GetString(1)));
  if(selections.Count!=count)throw new InvalidOperationException("Incomplete transmittal manifest.");
  var docs=selections.Select(x=>Document(t,project,x.Id,x.Hash,false)).ToArray();
  var intent=DocumentTransmittalReview.Prepare(project,recipient,purpose,docs);
  if(intent.ReviewHash!=hash)throw new InvalidOperationException("Transmittal manifest digest mismatch.");
  var rows=new List<StoredTransmittalEvent>();
  using(var q=Q(t,@"SELECT e.sequence,e.operation_key,e.previous_hash,e.event_hash,e.state,e.evidence_ref,e.note,e.actor_id,e.recorded_utc,e.audit_id,e.metadata_hash,a.id
   FROM project_transmittal_events e LEFT JOIN audit_events a ON a.id=e.audit_id AND a.actor_id=e.actor_id AND a.action='RecordTransmittalEvent'
   AND a.entity='Project' AND a.entity_id=$p WHERE e.intent_id=$i ORDER BY e.sequence",("$p",project),("$i",id)))
  using(var r=q.ExecuteReader())while(r.Read())
  {
   if(rows.Count>=4||r.IsDBNull(11))throw new InvalidOperationException("Transmittal audit missing.");
   var e=new TransmittalEvent(r.GetString(4),r.GetString(5),r.GetString(6),r.GetInt64(7),r.GetString(8),r.IsDBNull(2)?null:r.GetString(2),r.GetString(3));
   var row=new StoredTransmittalEvent(r.GetInt32(0),r.GetString(1),e,r.GetInt64(9),r.GetString(10));
   if(row.Sequence!=rows.Count+1||!ValidKey(row.Operation)||Metadata(id,operation,row)!=row.MetadataHash||
      (row.Sequence==1&&row.Operation!=operation))throw new InvalidOperationException("Transmittal event metadata mismatch.");
   rows.Add(row);
  }
  if(rows.Count==0)throw new InvalidOperationException("Unsealed transmittal.");
  DocumentTransmittalReview.Verify(intent,rows.Select(x=>x.Event).ToArray());
  return new(id,operation,intent,rows.AsReadOnly());
 }
 public StoredTransmittal Read(long project,long id)
 {
  using var t=db.BeginTransaction(deferred:true);_=Actor(t,project,"Read");return ReadCore(t,project,id);
 }
 StoredTransmittalEvent InsertEvent(SqliteTransaction t,long id,string intentOperation,TransmittalIntent intent,IReadOnlyList<StoredTransmittalEvent> rows,
  string key,string? expected,string state,string evidence,string note,long actor)
 {
  var e=DocumentTransmittalReview.Append(intent,rows.Select(x=>x.Event).ToArray(),expected,state,evidence,note,actor,DateTimeOffset.UtcNow,true);
  using var audit=Q(t,"INSERT INTO audit_events(actor_id,action,entity,entity_id,occurred_at) VALUES($a,'RecordTransmittalEvent','Project',$p,$u) RETURNING id",("$a",actor),("$p",intent.ProjectId),("$u",e.RecordedUtc));
  var row=new StoredTransmittalEvent(rows.Count+1,key,e,Convert.ToInt64(audit.ExecuteScalar()),"");
  row=row with{MetadataHash=Metadata(id,intentOperation,row)};
  Exec(t,@"INSERT INTO project_transmittal_events(intent_id,sequence,operation_key,previous_hash,event_hash,state,evidence_ref,note,actor_id,recorded_utc,audit_id,metadata_hash)
   VALUES($i,$seq,$k,$prev,$hash,$s,$e,$n,$a,$u,$audit,$meta)",("$i",id),("$seq",row.Sequence),("$k",key),("$prev",expected),("$hash",e.Hash),("$s",state),("$e",evidence),("$n",note),("$a",actor),("$u",e.RecordedUtc),("$audit",row.AuditId),("$meta",row.MetadataHash));
  return row;
 }
 public (StoredTransmittal Record,bool Replayed) Prepare(Guid operation,TransmittalIntent reviewed,string evidence,string note,bool confirmed)
 {
  if(!confirmed)throw new ArgumentException("Explicit preparation review required.");
  DocumentTransmittalReview.Verify(reviewed,Array.Empty<TransmittalEvent>());
  var key=Key(operation);var project=reviewed.ProjectId;
  using var t=db.BeginTransaction(deferred:false);var actor=Actor(t,project,"Prepared");
  var existing=Count(t,"SELECT coalesce((SELECT id FROM project_transmittal_intents WHERE operation_key=$k),0)",("$k",key));
  if(existing>0)
  {
   var old=ReadCore(t,project,existing);var first=old.History[0].Event;
   if(old.Intent.ReviewHash!=reviewed.ReviewHash||first.Actor!=actor||first.EvidenceRef!=evidence||first.Note!=note)throw new ArgumentException("Changed preparation replay.");
   return(old,true);
  }
  issueGate(t,project);
  var verified=DocumentTransmittalReview.Prepare(project,reviewed.Recipient,reviewed.Purpose,reviewed.Documents.Select(d=>Document(t,project,d.Id,d.Sha256,true)).ToArray());
  if(verified.ReviewHash!=reviewed.ReviewHash)throw new ArgumentException("Reviewed document manifest changed.");
  using var insert=Q(t,"INSERT INTO project_transmittal_intents(project_id,operation_key,intent_hash,recipient,purpose,expected_documents) VALUES($p,$k,$h,$r,$purpose,$count) RETURNING id",
   ("$p",project),("$k",key),("$h",verified.ReviewHash),("$r",verified.Recipient),("$purpose",verified.Purpose),("$count",verified.Documents.Count));
  var id=Convert.ToInt64(insert.ExecuteScalar());
  foreach(var d in verified.Documents)Exec(t,"INSERT INTO project_transmittal_documents VALUES($i,$d,$h)",("$i",id),("$d",d.Id),("$h",d.Sha256));
  var row=InsertEvent(t,id,key,verified,Array.Empty<StoredTransmittalEvent>(),key,null,"Prepared",evidence,note,actor);
  var read=ReadCore(t,project,id);if(read.History.Single()!=row)throw new InvalidOperationException("Preparation read-back failed.");
  t.Commit();return(read,false);
 }
 public (StoredTransmittalEvent Entry,bool Replayed) Append(long project,long id,Guid operation,string? expected,string state,string evidence,string note,bool confirmed)
 {
  if(!confirmed||state is not ("IssueRecorded" or "DeliveryRecorded" or "Withdrawn"))throw new ArgumentException("Explicit event review required.");
  var key=Key(operation);
  using var t=db.BeginTransaction(deferred:false);var actor=Actor(t,project,state);
  var history=ReadCore(t,project,id);
  if(history.History.SingleOrDefault(x=>x.Operation==key) is StoredTransmittalEvent old)
  {
   var e=old.Event;if(e.Actor!=actor||e.PreviousHash!=expected||e.State!=state||e.EvidenceRef!=evidence||e.Note!=note)throw new ArgumentException("Changed event replay.");
   return(old,true);
  }
  if(state=="IssueRecorded")
  {
   issueGate(t,project);
   foreach(var d in history.Intent.Documents)_=Document(t,project,d.Id,d.Sha256,true);
  }
  var row=InsertEvent(t,id,history.Operation,history.Intent,history.History,key,expected,state,evidence,note,actor);
  if(ReadCore(t,project,id).History[^1]!=row)throw new InvalidOperationException("Event read-back failed.");
  t.Commit();return(row,false);
 }
}
