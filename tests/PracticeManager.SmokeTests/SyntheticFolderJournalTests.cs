using Microsoft.Data.Sqlite;

// Test-only durable operation evidence. No production schema, adapter or filesystem command.
internal sealed class SyntheticFolderJournal
{
 readonly string connection;
 readonly SyntheticFolderProvisioning folders;
 readonly long project;
 public SyntheticFolderJournal(string path,SyntheticFolderProvisioning folders,long project)
 {
  if(project!=folders.ProjectId)throw new ArgumentException("Synthetic journal project identity mismatch.");
  if(!string.Equals(Path.GetFullPath(path),Path.Combine(folders.WorkspaceRoot,"synthetic-journal.db"),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Synthetic journal must use the single owned workspace journal.");
  this.folders=folders;this.project=project;
  connection=new SqliteConnectionStringBuilder{DataSource=path,ForeignKeys=true,Pooling=false,DefaultTimeout=10}.ToString();
  using var c=Open();
  using var q=c.CreateCommand();
  q.CommandText=@"
   CREATE TABLE IF NOT EXISTS attempts(
    id TEXT PRIMARY KEY, project_id INTEGER NOT NULL CHECK(project_id>0),
    plan_hash TEXT NOT NULL CHECK(length(plan_hash)=64),
    review_hash TEXT NOT NULL CHECK(length(review_hash)=64),
    predecessor TEXT UNIQUE REFERENCES attempts(id),
    expected_count INTEGER NOT NULL CHECK(expected_count>0),
    expected_missing INTEGER NOT NULL CHECK(expected_missing BETWEEN 0 AND expected_count));
   CREATE TABLE IF NOT EXISTS outcomes(
    attempt_id TEXT PRIMARY KEY REFERENCES attempts(id),
    state TEXT NOT NULL CHECK(state IN ('Confirmed','Superseded')),
    created_count INTEGER, present_count INTEGER,
    CHECK((state='Confirmed' AND created_count>=0 AND present_count>=0 AND created_count IS NOT NULL AND present_count IS NOT NULL)
       OR (state='Superseded' AND created_count IS NULL AND present_count IS NULL)));
   CREATE TRIGGER IF NOT EXISTS attempt_no_update BEFORE UPDATE ON attempts BEGIN SELECT RAISE(ABORT,'Immutable attempt'); END;
   CREATE TRIGGER IF NOT EXISTS attempt_no_delete BEFORE DELETE ON attempts BEGIN SELECT RAISE(ABORT,'Retained attempt'); END;
   CREATE TRIGGER IF NOT EXISTS outcome_no_update BEFORE UPDATE ON outcomes BEGIN SELECT RAISE(ABORT,'Immutable outcome'); END;
   CREATE TRIGGER IF NOT EXISTS outcome_no_delete BEFORE DELETE ON outcomes BEGIN SELECT RAISE(ABORT,'Retained outcome'); END;
   CREATE TRIGGER IF NOT EXISTS attempt_one_open BEFORE INSERT ON attempts WHEN EXISTS(
    SELECT 1 FROM attempts a WHERE a.project_id=NEW.project_id AND NOT EXISTS(SELECT 1 FROM outcomes o WHERE o.attempt_id=a.id))
    BEGIN SELECT RAISE(ABORT,'Open attempt'); END;
   CREATE TRIGGER IF NOT EXISTS outcome_counts BEFORE INSERT ON outcomes WHEN NEW.state='Confirmed' AND NOT EXISTS(
    SELECT 1 FROM attempts a WHERE a.id=NEW.attempt_id AND a.expected_missing=NEW.created_count AND a.expected_count=NEW.created_count+NEW.present_count)
    BEGIN SELECT RAISE(ABORT,'Count evidence mismatch'); END;";
  q.ExecuteNonQuery();
 }
 SqliteConnection Open()
 {
  var c=new SqliteConnection(connection);
  c.Open();
  using var q=c.CreateCommand();q.CommandText="PRAGMA synchronous=FULL";q.ExecuteNonQuery();
  return c;
 }
 static SqliteCommand Command(SqliteConnection c,SqliteTransaction? t,string sql,params (string Key,object? Value)[] values)
 {
  var q=c.CreateCommand();q.Transaction=t;q.CommandText=sql;
  foreach(var (key,value) in values)q.Parameters.AddWithValue(key,value??DBNull.Value);
  return q;
 }
 static void Execute(SqliteConnection c,SqliteTransaction t,string sql,params (string Key,object? Value)[] values)
 {using var q=Command(c,t,sql,values);q.ExecuteNonQuery();}
 public string? Pending()
 {
  using var c=Open();using var q=Command(c,null,"SELECT a.id FROM attempts a WHERE a.project_id=$p AND NOT EXISTS(SELECT 1 FROM outcomes o WHERE o.attempt_id=a.id)",("$p",project));
  return q.ExecuteScalar() as string;
 }
 public string State(string id)
 {
  using var c=Open();using var q=Command(c,null,"SELECT COALESCE(o.state,'Uncertain') FROM attempts a LEFT JOIN outcomes o ON o.attempt_id=a.id WHERE a.id=$i AND a.project_id=$p",("$i",id),("$p",project));
  return q.ExecuteScalar() as string??"Unknown";
 }
 public string ExecuteReviewed(bool correspondence,bool restricted,string reviewHash,bool confirmed,string? predecessor=null,
  Action<int>? afterCreate=null,Action? beforeOutcome=null)
 {
  if(!confirmed)throw new ArgumentException("Explicit review required.");
  // Hold the same workspace-wide OS handle through intent, all physical effects and outcome.
  using var fence=SyntheticFolderOperationFence.Acquire(folders.WorkspaceRoot);
  var review=folders.Preview(correspondence,restricted);
  if(review.ReviewFingerprint!=reviewHash)throw new ArgumentException("Stale review.");
  var id=Guid.NewGuid().ToString("N");
  // Commit intent BEFORE directory creation. Missing outcome always means uncertain.
  using(var c=Open())
  using(var t=c.BeginTransaction(deferred:false))
  {
   using(var q=Command(c,t,"SELECT a.id,a.plan_hash FROM attempts a WHERE a.project_id=$p AND NOT EXISTS(SELECT 1 FROM outcomes o WHERE o.attempt_id=a.id)",("$p",project)))
   using(var reader=q.ExecuteReader())
   {
    if(reader.Read())
    {
     if(predecessor!=reader.GetString(0)||review.PlanFingerprint!=reader.GetString(1))
      throw new ArgumentException("Uncertain operation requires explicit same-plan recovery.");
    }
    else if(predecessor is not null)throw new ArgumentException("No matching uncertain operation.");
   }
   if(predecessor is not null)Execute(c,t,"INSERT INTO outcomes(attempt_id,state) VALUES($i,'Superseded')",("$i",predecessor));
   Execute(c,t,"INSERT INTO attempts VALUES($i,$p,$h,$r,$prior,$total,$missing)",
    ("$i",id),("$p",project),("$h",review.PlanFingerprint),("$r",reviewHash),("$prior",predecessor),
    ("$total",review.Steps.Count),("$missing",review.Steps.Count(x=>!x.Exists)));
   t.Commit();
  }
  var result=folders.Apply(correspondence,restricted,reviewHash,true,afterCreate);
  var completed=folders.Preview(correspondence,restricted);
  beforeOutcome?.Invoke();
  var final=folders.Preview(correspondence,restricted);
  if(final.PlanFingerprint!=review.PlanFingerprint||final.Steps.Any(x=>!x.Exists)||
     final.ReviewFingerprint!=completed.ReviewFingerprint)throw new IOException("Folder outcome no longer matches the reviewed plan.");
  using(var c=Open())
  using(var t=c.BeginTransaction(deferred:false))
  {
   Execute(c,t,"INSERT INTO outcomes VALUES($i,'Confirmed',$n,$e)",("$i",id),("$n",result.Created),("$e",result.AlreadyPresent));
   t.Commit();
  }
  return id;
 }
}

internal static class SyntheticFolderJournalTests
{
 public static void Run(string ownedRoot,Action<bool,string> check)
 {
  var root=Path.Combine(ownedRoot,"folder-journal-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(root);
  var production=Directory.CreateDirectory(Path.Combine(root,"SyntheticProduction")).FullName;
  Directory.CreateDirectory(Path.Combine(root,"SyntheticRestricted"));
  using(var lockFile=new FileStream(Path.Combine(root,SyntheticFolderOperationFence.FileName),FileMode.CreateNew,FileAccess.Write,FileShare.None))lockFile.Flush(true);
  var folders=new SyntheticFolderProvisioning(root,11,"PRJ-000011");
  var path=Path.Combine(root,"synthetic-journal.db");
  var journal=new SyntheticFolderJournal(path,folders,11);
  void Reject<T>(Action action,string label) where T:Exception
  {try{action();}catch(T){check(true,label);return;}throw new Exception("FAIL: "+label);}
  check(SyntheticFolderOperationFence.ProbeInOtherProcess(root)==0,"separate process can acquire free synthetic folder fence");
  using(var held=SyntheticFolderOperationFence.Acquire(root))
  {
   check(SyntheticFolderOperationFence.ProbeInOtherProcess(root)==23,"separate process denied while folder fence is held");
   Reject<IOException>(()=>journal.ExecuteReviewed(false,false,folders.Preview(false,false).ReviewFingerprint,true),"held workspace fence blocks journal execution");
   check(journal.Pending()==null,"blocked execution records no intent");
  }
  check(SyntheticFolderOperationFence.ProbeInOtherProcess(root)==0,"folder fence released after handle disposal");
  Reject<ArgumentException>(()=>new SyntheticFolderJournal(path,folders,12),"journal rejects mismatched project identity");
  Reject<ArgumentException>(()=>new SyntheticFolderJournal(Path.Combine(root,"alternate.db"),folders,11),"alternate journal cannot bypass uncertainty");
  var review=folders.Preview(false,false);
  Reject<ArgumentException>(()=>journal.ExecuteReviewed(false,false,review.ReviewFingerprint,false),"journal confirmation required");
  check(journal.Pending()==null,"unconfirmed folder review records no intent");
  Reject<IOException>(()=>journal.ExecuteReviewed(false,false,review.ReviewFingerprint,true,afterCreate:n=>{
   check(journal.Pending()!=null,"folder intent durable before external effect");
   if(n==1)
   {
    check(SyntheticFolderOperationFence.ProbeInOtherProcess(root)==23,"folder fence held during physical effect");
    var current=folders.Preview(false,false);
    var active=journal.Pending()!;
    Reject<IOException>(()=>journal.ExecuteReviewed(false,false,current.ReviewFingerprint,true,active),"running operation cannot be superseded");
   }
   if(n==3)throw new IOException("Synthetic interruption");
  }),"folder journal interruption leaves uncertainty");
  check(SyntheticFolderOperationFence.ProbeInOtherProcess(root)==0,"interrupted operation releases fence without clearing uncertainty");
  var first=journal.Pending()!;
  check(first!=null&&journal.State(first)=="Uncertain","interrupted intent remains open");
  journal=new SyntheticFolderJournal(path,folders,11);
  check(journal.Pending()==first&&journal.State(first)=="Uncertain","uncertainty survives reopen");
  var fresh=folders.Preview(false,false);
  Reject<ArgumentException>(()=>journal.ExecuteReviewed(false,false,fresh.ReviewFingerprint,true),"uncertain attempt blocks blind replay");
  Reject<ArgumentException>(()=>journal.ExecuteReviewed(false,false,review.ReviewFingerprint,true,first),"stale repair review rejected");
  var changed=folders.Preview(true,false);
  Reject<ArgumentException>(()=>journal.ExecuteReviewed(true,false,changed.ReviewFingerprint,true,first),"uncertain repair cannot silently change plan");
  var otherFolders=new SyntheticFolderProvisioning(root,12,"PRJ-000012");
  var other=new SyntheticFolderJournal(path,otherFolders,12);
  Reject<ArgumentException>(()=>other.ExecuteReviewed(false,false,otherFolders.Preview(false,false).ReviewFingerprint,true,first),"uncertain repair cannot cross project");
  var second=journal.ExecuteReviewed(false,false,fresh.ReviewFingerprint,true,first);
  check(journal.State(first)=="Superseded"&&journal.State(second)=="Confirmed"&&journal.Pending()==null,"repair records linked durable confirmation");
  check(Directory.GetDirectories(Path.Combine(production,"PRJ-000011")).Length==9,"journal repair verifies complete production tree");
  // Simulate lost acknowledgement AFTER successful filesystem execution.
  var next=folders.Preview(true,false);
  Reject<IOException>(()=>journal.ExecuteReviewed(true,false,next.ReviewFingerprint,true,beforeOutcome:()=>throw new IOException("Synthetic lost acknowledgement")),
   "lost folder acknowledgement is not success");
  var third=journal.Pending()!;
  check(third!=null&&journal.State(third)=="Uncertain","completed filesystem without outcome remains uncertain");
  var allPresent=folders.Preview(true,false);
  check(allPresent.Steps.All(s=>s.Exists),"lost acknowledgement requires fresh observation");
  var fourth=journal.ExecuteReviewed(true,false,allPresent.ReviewFingerprint,true,third);
  check(journal.State(third)=="Superseded"&&journal.State(fourth)=="Confirmed"&&journal.Pending()==null,"observed existing tree confirms reviewed repair without duplicate creation");
  // Simulate disappearance between filesystem verification and outcome commit.
  var finalReview=folders.Preview(true,false);
  var missing=Path.Combine(production,"PRJ-000011","Correspondence");
  Reject<IOException>(()=>journal.ExecuteReviewed(true,false,finalReview.ReviewFingerprint,true,beforeOutcome:()=>Directory.Delete(missing,false)),
    "changed physical state before confirmation leaves uncertain outcome");
  var fifth=journal.Pending()!;
  check(journal.State(fifth)=="Uncertain","missing folder does not receive confirmed outcome");
  var latest=folders.Preview(true,false);
  var sixth=journal.ExecuteReviewed(true,false,latest.ReviewFingerprint,true,fifth);
  check(journal.State(fifth)=="Superseded"&&journal.State(sixth)=="Confirmed"&&Directory.Exists(missing),"fresh review repairs disappearance");
  using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false,ForeignKeys=true}.ToString());
  c.Open();using var q=c.CreateCommand();
  q.CommandText="SELECT count(*) FROM attempts";check(Convert.ToInt64(q.ExecuteScalar())==6,"journal retains all attempt identities");
  q.CommandText="SELECT count(*) FROM outcomes WHERE state='Confirmed' AND created_count=0";check(Convert.ToInt64(q.ExecuteScalar())==1,"zero-create recovery recorded");
  q.CommandText="DELETE FROM attempts";Reject<SqliteException>(()=>q.ExecuteNonQuery(),"journal attempt evidence immutable");
  q.CommandText="UPDATE outcomes SET state='Superseded'";Reject<SqliteException>(()=>q.ExecuteNonQuery(),"journal outcome evidence immutable");
 }
}
