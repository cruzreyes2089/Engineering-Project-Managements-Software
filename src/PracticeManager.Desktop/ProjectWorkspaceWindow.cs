using System.Windows;
using System.Windows.Controls;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class ProjectWorkspaceWindow:Window
{
 readonly LocalPractice service;readonly long project;
 readonly TextBlock heading=new(){FontSize=24,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
 readonly TextBlock summary=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
 readonly List<(Button Button,Func<bool> Allowed)> actions=new();
 public ProjectWorkspaceWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="Project workspace";Width=680;SizeToContent=SizeToContent.Height;
  MaxHeight=SystemParameters.WorkArea.Height-40;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
  var p=new StackPanel{Margin=new Thickness(20)};p.Children.Add(heading);p.Children.Add(summary);
  void Group(string title)=>p.Children.Add(new TextBlock{Text=title,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,4)});
  void Add(string label,Func<bool> allowed,Func<Window> open)
  {
   var b=new Button{Content=label,HorizontalContentAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,5),IsEnabled=false};
   actions.Add((b,allowed));p.Children.Add(b);
   b.Click+=(_,_)=>{
    try{_=service.ReadProjectOverview(project);if(!allowed())throw new UnauthorizedAccessException();var w=open();w.Owner=this;w.ShowDialog();Refresh();}
    catch(UnauthorizedAccessException){Refresh();status.Text="Access denied.";}
    catch(Exception){Refresh();status.Text="Unable to open this project action.";}
   };
  }
  Group("Project work");
  Add("Phases and tasks",()=>service.Can("Projects.Plan")&&service.Can("Employees.View"),()=>new ProjectPlanningWindow(service,project));
  Add("Documents",()=>service.Can("Documents.View"),()=>new ProjectDocumentsWindow(service,project));
  Add("QA/QC and additional scope",()=>service.Can("QA.View"),()=>new ProjectQualityWindow(service,project));
  Group("Project administration");
  Add("Project setup and agreement",()=>service.Can("Projects.View"),()=>new ProjectHandoffWindow(service,project));
  Add("Project contacts",()=>service.Can("Contacts.View"),()=>new ProjectPartiesWindow(service,project));
  Add("Conflict screening",()=>service.Can("Compliance.View"),()=>new ConflictReviewWindow(service,project));
  Add("Close or reopen project",()=>service.Can("Projects.Closeout")&&service.Can("Reports.Operations"),()=>new ProjectLifecycleWindow(service,project));
  p.Children.Add(status);var bar=new WrapPanel();WorkWindowFields.Button(bar,"Refresh",Refresh);WorkWindowFields.Button(bar,"Close",Close);p.Children.Add(bar);
  Content=new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Refresh();
 }
 void Refresh()
 {
  heading.Text="Project";summary.Text="";status.Text="";
  foreach(var a in actions)a.Button.IsEnabled=false;
  try{
   var row=service.ReadProjectOverview(project);
   heading.Text=row.Number+" — "+row.Title;
   summary.Text="Client: "+row.ClientName+"\nSite: "+row.Site+"\nStatus: "+row.State+"   |   Setup: "+row.SetupState+"\nConflict screening: "+row.ConflictStatus;
   foreach(var a in actions)a.Button.IsEnabled=a.Allowed();
  }catch(UnauthorizedAccessException){Clear();status.Text="Access denied.";}
   catch(Exception){Clear();status.Text="Project unavailable. Refresh before continuing.";}
 }
 void Clear(){heading.Text="Project";summary.Text="";foreach(var a in actions)a.Button.IsEnabled=false;
 }
}
