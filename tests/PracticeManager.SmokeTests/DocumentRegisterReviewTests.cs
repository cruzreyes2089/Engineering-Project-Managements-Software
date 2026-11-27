using PracticeManager.Core;
internal static class DocumentRegisterReviewTests
{
 public static void Run(string root,Action<bool,string> check)
 {
  var external=Path.Combine(root,"synthetic-folder-policy");Directory.CreateDirectory(external);
  var projectRoot=Path.Combine(external,"projects");Directory.CreateDirectory(projectRoot);
  var dbRoot=Path.Combine(external,"database");Directory.CreateDirectory(dbRoot);
  var db=Path.Combine(dbRoot,"synthetic.db");
  Directory.CreateDirectory(Path.Combine(projectRoot,"A","Drawings"));Directory.CreateDirectory(Path.Combine(projectRoot,"B"));
  var a=ProjectFolderPolicy.Review(projectRoot,"A/Drawings",db);
  check(a.RelativePath=="A/Drawings"&&a.CanonicalPath==Path.Combine(projectRoot,"A","Drawings"),"folder review binds relative local project path");
  check(ProjectFolderPolicy.Overlaps(Path.Combine(projectRoot,"A"),a.CanonicalPath)&&!ProjectFolderPolicy.Overlaps(a.CanonicalPath,Path.Combine(projectRoot,"B")),"folder overlap separates project directory trees");
  void Invalid(Action action,string label){try{action();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  Invalid(()=>ProjectFolderPolicy.Review(projectRoot,"../database",db),"folder traversal refused");
  Invalid(()=>ProjectFolderPolicy.Review(projectRoot,"A//Drawings",db),"ambiguous folder components refused");
  Invalid(()=>ProjectFolderPolicy.Review(projectRoot,Path.Combine(projectRoot,"B"),db),"absolute folder input refused");
  Invalid(()=>ProjectFolderPolicy.Review(external,"projects/B",db),"database ancestor cannot be external root");
  Invalid(()=>ProjectFolderPolicy.Review(projectRoot,"Missing",db),"folder review never creates directory");
  var defaults=ProjectFolderTemplate.Preview(ProjectFolderTemplate.Defaults);
  check(defaults.SequenceEqual(new[]{"Architectural","Calculations","Construction Administration","Plancheck Comment Responses","QA-QC","Reference Documents","Send Outs","Site Pictures","Structural DWGs"}),"CRC preferred folder names are the proposed default template");
  var structuralOnly=ProjectFolderTemplate.Defaults.Select(x=>x with{IncludedByDefault=x.Name is "Structural DWGs" or "Calculations" or "Site Pictures" or "Send Outs"}).ToArray();
  check(ProjectFolderTemplate.Preview(structuralOnly).Count==4,"project template preview supports discipline-specific selection");
  check(defaults.Contains("QA-QC")&&!ProjectFolderTemplate.Defaults.Any(x=>x.Name is "Mechanical" or "Electrical" or "Plumbing"),"QA-QC is standard and consultant MEP belongs in reference documents");
  var custom=ProjectFolderTemplate.Defaults.Append(new ProjectFolderTemplateItem("Mechanical Design",true)).ToArray();
  check(ProjectFolderTemplate.Preview(custom).Contains("Mechanical Design"),"custom template allows contracted production disciplines");
  var optional=ProjectFolderTemplate.Defaults.Select(x=>x.Name=="Correspondence"?x with{IncludedByDefault=true}:x).ToArray();
  check(ProjectFolderTemplate.Preview(optional).Contains("Correspondence"),"correspondence remains selectable");
  Invalid(()=>ProjectFolderTemplate.Preview(new[]{new ProjectFolderTemplateItem("Drawings",true),new ProjectFolderTemplateItem("drawings",false)}),"folder template rejects case-alias duplicates");
  Invalid(()=>ProjectFolderTemplate.Preview(new[]{new ProjectFolderTemplateItem("../Secret",true)}),"folder template rejects traversal");
  Invalid(()=>ProjectFolderTemplate.Preview(new[]{new ProjectFolderTemplateItem("COM¹",true)}),"folder template rejects Windows device names");
  Invalid(()=>ProjectFolderTemplate.Preview(new[]{new ProjectFolderTemplateItem("QA-QC",false)}),"empty selected template refused");
  check(!Directory.Exists(Path.Combine(projectRoot,"Architectural")),"template preview has no filesystem side effects");

  var d=new ReviewedIssueDocument(1,new string('a',64),"Drawing.pdf","Drawings",100);
  var intent=DocumentTransmittalReview.Prepare(1,"Synthetic recipient","Permit review",new[]{d});
  var now=new DateTimeOffset(2026,11,27,0,0,0,TimeSpan.Zero);var none=Array.Empty<TransmittalEvent>();
  var prepared=DocumentTransmittalReview.Append(intent,none,null,"Prepared","Review evidence","Prepared only",1,now,true);
  Invalid(()=>DocumentTransmittalReview.Append(intent,new[]{prepared},prepared.Hash,"DeliveryRecorded","Delivery evidence","Review",1,now,true),"delivery cannot precede recorded issue");
  Invalid(()=>DocumentTransmittalReview.Append(intent,new[]{prepared},null,"IssueRecorded","Issue evidence","Review",1,now,true),"stale transmittal predecessor refused");
  Invalid(()=>DocumentTransmittalReview.Append(intent,new[]{prepared},prepared.Hash,"IssueRecorded","Issue evidence","Review",1,now,false),"explicit issuance review required");
  var issued=DocumentTransmittalReview.Append(intent,new[]{prepared},prepared.Hash,"IssueRecorded","Synthetic external issue record","Reviewed independently",2,now.AddMinutes(1),true);
  var delivered=DocumentTransmittalReview.Append(intent,new[]{prepared,issued},issued.Hash,"DeliveryRecorded","Synthetic external receipt","Reviewed independently",2,now.AddMinutes(2),true);
  var history=new[]{prepared,issued,delivered};DocumentTransmittalReview.Verify(intent,history);
  check(delivered.PreviousHash==issued.Hash&&issued.PreviousHash==prepared.Hash,"issue and delivery remain separate reviewed history");
  var withdrawn=DocumentTransmittalReview.Append(intent,history,delivered.Hash,"Withdrawn","Synthetic withdrawal record","Superseded",2,now.AddMinutes(3),true);
  Invalid(()=>DocumentTransmittalReview.Append(intent,history.Append(withdrawn).ToArray(),withdrawn.Hash,"IssueRecorded","Again","Review",2,now.AddMinutes(4),true),"withdrawn transmittal cannot silently reissue");
  Invalid(()=>DocumentTransmittalReview.Verify(intent with{Recipient="Other"},history),"transmittal identity binds recipient");
  Invalid(()=>DocumentTransmittalReview.Verify(intent,new[]{prepared,issued with{EvidenceRef="Changed"}}),"transmittal digest detects changed evidence");
  Invalid(()=>DocumentTransmittalReview.Prepare(1,"Recipient","Review",new[]{d,d}),"duplicate issue versions rejected");
  Invalid(()=>DocumentTransmittalReview.Prepare(1,"Recipient","Review",new[]{d with{Category="Contracts"}}),"ineligible document category rejected");
 }
}
