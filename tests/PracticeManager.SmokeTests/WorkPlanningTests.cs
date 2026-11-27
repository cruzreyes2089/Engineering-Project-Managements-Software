using PracticeManager.Core;
using PracticeManager.Persistence;
internal static class WorkPlanningTests
{
 public static void Run(string path,string pw,Action<bool,string> check)
 {
  var s=new LocalPractice(path);s.Bootstrap("Synthetic Work Firm","admin",pw,"CL");check(s.Login("admin",pw),"work planner admin login");
  void Invalid(Action a,string label){try{a();}catch(ArgumentException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  void Denied(Action a,string label){try{a();}catch(UnauthorizedAccessException){check(true,label);return;}throw new Exception("FAIL: "+label);}
  var c=s.SaveClient(new Client(0,"","Business / Company","Synthetic Client","","","","","","","","","","","Active"));
  var id=s.CreateProposal(new(c,"Synthetic","Site","Scope",100m));var hash=ConflictReviewTests.Issue(s,id,1);var project=s.RecordProposalDecision(id,hash,true,"Synthetic").ProjectId!.Value;
  var role=s.ReadStaffRoles().Single(r=>r.Name=="Engineer");s.SetStaffGrant(role.Id,"Work.Own",true,false);
  var u=s.CreateStaffAccount("worker",role.Id,"Synthetic-worker-temp-0001");var e=s.CreateEmployee(new("Synthetic","Worker","","",true),u);
  var other=s.CreateEmployee(new("Other","Employee","","",true));var inactive=s.CreateEmployee(new("Inactive","Employee","","",false));
  var worker=new LocalPractice(path);check(worker.ChangeRequiredPassword("worker","Synthetic-worker-temp-0001","Synthetic-worker-password-0001")&&worker.Login("worker","Synthetic-worker-password-0001"),"worker authenticated");
  var day=new DateOnly(2026,11,26);var phase=s.CreateProjectPhase(project,"Design",day,day.AddDays(10));
  Invalid(()=>s.CreateProjectPhase(project,"design",day,day.AddDays(10)),"duplicate phase name blocked");
  var plan=new ProjectTaskPlan(phase,e,"Structural design",day,day.AddDays(3),120);
  Invalid(()=>s.CreateProjectTask(project,plan with{EmployeeId=inactive}),"inactive employee assignment blocked");
  Invalid(()=>s.CreateProjectTask(project,plan with{Start=day.AddDays(-1)}),"out-of-phase date blocked");
  var task=s.CreateProjectTask(project,plan);var otherTask=s.CreateProjectTask(project,plan with{EmployeeId=other,Title="Other task"});
  check(s.ReadStaffSchedule(day.AddDays(3),day.AddDays(3)).Count==2&&s.ReadStaffSchedule(day.AddDays(4),day.AddDays(4)).Count==0,"inclusive overlap date boundaries");
  check(s.ReadStaffSchedule(day,day.AddDays(5),e).Single().Id==task,"schedule filters employee");
  check(worker.ReadMyWork(day,day.AddDays(10)).Single().Id==task,"own work scoped to linked employee");
  Denied(()=>worker.ReadProjectTasks(project),"own work grant does not grant planning register");
  Denied(()=>worker.ReadStaffSchedule(day,day.AddDays(10)),"own work grant does not grant staff schedule");
  Invalid(()=>worker.AdvanceMyTask(task,1,"InProgress"),"pending project cannot start task");
  Invalid(()=>s.EditProjectPhase(project,phase,1,"Design",day,day.AddDays(1)),"phase cannot exclude existing tasks");
  s.EditProjectTask(project,task,1,plan with{EstimateMinutes=180});
  Invalid(()=>s.EditProjectTask(project,task,1,plan),"stale plan revision blocked");
  var a=s.RecordSignedAgreement(project,hash,"test.pdf","%PDF-1.7\nSynthetic only\n%%EOF"u8.ToArray(),"Synthetic review",true);
  ConflictReviewTests.Clear(s,project);
  check(s.AuthorizeProjectStart(project,hash,a.Id,a.PdfHash,true,"Synthetic")=="Active","project explicitly authorized for work");
  Invalid(()=>worker.AdvanceMyTask(otherTask,1,"InProgress"),"cannot start another employee task");
  Invalid(()=>worker.AdvanceMyTask(task,2,"Completed"),"cannot skip task start");
  worker.AdvanceMyTask(task,2,"InProgress");check(worker.ReadMyWork(day,day.AddDays(10)).Single().State=="InProgress","own task started");
  Invalid(()=>s.EditProjectTask(project,task,3,plan),"in-progress task cannot be reassigned or edited");
  worker.AdvanceMyTask(task,3,"Completed");check(s.ReadProjectTasks(project).Single(x=>x.Id==task).State=="Completed","own task completion retained");
  var change=s.ProposeScopeChange(project,"Extra design","Synthetic scope expansion");
  var extra=s.CreateProjectTask(project,plan with{Title="Extra design",ScopeChangeId=change});
  check(worker.ReadMyWork(day,day.AddDays(10)).Single(x=>x.Id==extra).ScopeState=="Proposed","employee sees proposed scope state");
  Invalid(()=>worker.AdvanceMyTask(extra,1,"InProgress"),"unapproved additional scope cannot start");
  Invalid(()=>s.EditProjectTask(project,extra,1,plan),"additional scope cannot be removed to bypass authorization");
  check(s.DecideScopeChange(project,change,true,"Reviewed written synthetic authorization",true)=="Authorized","additional scope authorized");
  worker.AdvanceMyTask(extra,1,"InProgress");check(worker.ReadMyWork(day,day.AddDays(10)).Single(x=>x.Id==extra).ScopeState=="Authorized","authorized extra task starts");
  var rejected=s.ProposeScopeChange(project,"Rejected additional","Synthetic");var rejectedTask=s.CreateProjectTask(project,plan with{ScopeChangeId=rejected});
  s.DecideScopeChange(project,rejected,false,"Client declined",false);
  Invalid(()=>worker.AdvanceMyTask(rejectedTask,1,"InProgress"),"rejected additional scope cannot start");
  Invalid(()=>s.CreateProjectTask(project,plan with{ScopeChangeId=rejected}),"rejected scope cannot receive new tasks");
  var p2=s.CreateProposal(new(c,"Other project","Site","Scope",100m));var h2=ConflictReviewTests.Issue(s,p2,1);var project2=s.RecordProposalDecision(p2,h2,true,"Synthetic").ProjectId!.Value;
  var phase2=s.CreateProjectPhase(project2,"Design",day,day.AddDays(10));
  Invalid(()=>s.CreateProjectTask(project2,plan with{PhaseId=phase2,ScopeChangeId=change}),"cross-project scope rejected");
  check(s.ReadTaskCancellation(project,rejectedTask)==null,"planned task has no invented cancellation");
  Denied(()=>worker.CancelPlannedTask(project,rejectedTask,1,"Unauthorized",true),"employee cannot cancel planned task");
  Invalid(()=>s.CancelPlannedTask(project2,rejectedTask,1,"Wrong project",true),"cross-project cancellation blocked");
  Invalid(()=>s.CancelPlannedTask(project,rejectedTask,2,"Stale revision",true),"stale task cancellation blocked");
  Invalid(()=>s.CancelPlannedTask(project,rejectedTask,1,"Not confirmed",false),"explicit task cancellation required");
  Invalid(()=>s.CancelPlannedTask(project,extra,2,"Started task",true),"started task cannot be cancelled in planned-task workflow");
  Invalid(()=>s.CancelPlannedTask(project,task,4,"Completed task",true),"completed task cannot be cancelled");
  var cancelled=s.CancelPlannedTask(project,rejectedTask,1,"Client declined additional scope",true);
  check(cancelled.TaskId==rejectedTask&&cancelled.PlanRevision==1&&cancelled.Reason=="Client declined additional scope"&&s.ReadTaskCancellation(project,rejectedTask)==cancelled,"cancellation retains reviewed plan revision and evidence");
  check(s.ReadProjectTasks(project).Single(x=>x.Id==rejectedTask).State=="Cancelled"&&worker.ReadMyWork(day,day.AddDays(10)).Single(x=>x.Id==rejectedTask).State=="Cancelled","cancelled task retained in project and own work history");
  check(s.ReadStaffSchedule(day,day.AddDays(10)).Single(x=>x.Id==rejectedTask).State=="Cancelled","schedule exposes cancellation rather than deleting assignment");
  check(s.ReadProjectCloseoutReview(project).OpenTasks==2,"cancelled rejected-scope task no longer blocks closeout");
  var operations=s.ReadProjectOperations(project,day,day.AddDays(10));
  check(operations.CancelledTasks==1&&operations.PlannedTasks==1&&operations.StartedTasks==1&&operations.CompletedTasks==1&&operations.EstimatedMinutes==420,"operations segregates cancelled task and excludes cancelled estimate");
  Invalid(()=>s.CancelPlannedTask(project,rejectedTask,1,"Repeat",true),"repeat cancellation rejected");
  Invalid(()=>s.EditProjectTask(project,rejectedTask,1,plan with{ScopeChangeId=rejected}),"cancelled task cannot be edited");
  var baseCancel=s.CreateProjectTask(project,plan with{Title="Unneeded base work"});
  s.CancelPlannedTask(project,baseCancel,1,"No longer required",true);
  Invalid(()=>worker.AdvanceMyTask(baseCancel,1,"InProgress"),"cancelled base task cannot start");
  Denied(()=>worker.ReadTaskCancellation(project,rejectedTask),"ordinary employee cannot read management cancellation evidence");
  var employee=s.ReadEmployees().Single(x=>x.Id==e);s.EditEmployee(e,employee.Revision,new(employee.First,employee.Last,employee.Email,employee.Title,false));Denied(()=>worker.ReadMyWork(day,day.AddDays(10)),"inactive linked employee denied work access");
 }
}
