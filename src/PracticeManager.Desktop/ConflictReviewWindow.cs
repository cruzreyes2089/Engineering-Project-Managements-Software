using System.Windows;
using System.Windows.Controls;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class ConflictReviewWindow:Window
{
 readonly LocalPractice service;readonly long project;readonly int? proposalRevision;readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};readonly DataGrid grid=WorkWindowFields.Grid();
 readonly TextBox jurisdiction=new(){MaxLength=300},evidence=new(){MaxLength=4000,AcceptsReturn=true,Height=110,TextWrapping=TextWrapping.Wrap};
 readonly ComboBox[] answers=Enumerable.Range(0,4).Select(_=>new ComboBox{ItemsSource=new[]{"Unknown","Yes","No"},SelectedIndex=0}).ToArray();
 readonly CheckBox confirm=new(){Content="I reviewed the current project facts and supporting evidence."};long? expected;
 public ConflictReviewWindow(LocalPractice service,long project,int? proposalRevision=null)
 {
  this.service=service;this.project=project;this.proposalRevision=proposalRevision;Title=proposalRevision.HasValue?"Proposal conflict screening":"Project conflict screening";Width=880;Height=730;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var p=new StackPanel{Margin=new Thickness(16)};p.Children.Add(new TextBlock{Text=Title,FontSize=23,FontWeight=FontWeights.SemiBold});p.Children.Add(new TextBlock{Text="Any Yes or Unknown blocks proposal issue or new project/task authorization, as applicable. No automatic recusal or legal clearance. Record written evidence; do not enter confidential public-employer information.",TextWrapping=TextWrapping.Wrap});p.Children.Add(status);
  p.Children.Add(new TextBlock{Text="Verified permitting jurisdiction"});p.Children.Add(jurisdiction);
  var labels=new[]{"Is the site or permit within a restricted public-employer jurisdiction?","Is there public duty, review, decision or other employer involvement?","Would public resources, time or nonpublic information be used?","Is any other actual or potential conflict unresolved?"};
  for(int i=0;i<answers.Length;i++){p.Children.Add(new TextBlock{Text=labels[i],TextWrapping=TextWrapping.Wrap});p.Children.Add(answers[i]);}
  p.Children.Add(new TextBlock{Text="Review evidence / declarations / reason for changed facts"});p.Children.Add(evidence);p.Children.Add(confirm);var bar=new WrapPanel();WorkWindowFields.Button(bar,"Record new review",()=>Run(Save));WorkWindowFields.Button(bar,"Refresh",()=>Run(Refresh));p.Children.Add(bar);
  grid.Height=210;WorkWindowFields.Columns(grid,("Review","Id"),("Jurisdiction","Jurisdiction"),("Restricted","RestrictedJurisdiction"),("Duty overlap","PublicDutyOverlap"),("Resources","PublicResources"),("Other","OtherConflict"),("Result","Disposition"),("Evidence","Evidence"),("UTC","ReviewedUtc"));p.Children.Add(grid);Content=new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Run(Refresh);
 }
 void Run(Action a){try{a();}catch(UnauthorizedAccessException){grid.ItemsSource=null;status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Review unavailable. Refresh before retrying.";}}
 void Refresh(){grid.ItemsSource=null;var history=proposalRevision.HasValue?service.ReadProposalConflictHistory(project):service.ReadConflictHistory(project);grid.ItemsSource=history;expected=history.FirstOrDefault()?.Id;status.Text="Current screening: "+(proposalRevision.HasValue?service.ReadProposalConflictStatus(project,proposalRevision.Value):service.ReadProjectConflictStatus(project));jurisdiction.Text="";evidence.Clear();foreach(var answer in answers)answer.SelectedIndex=0;confirm.IsChecked=false;}
 void Save(){if(!service.Can("Compliance.Record"))throw new UnauthorizedAccessException();var input=new ConflictReviewInput(jurisdiction.Text,(string)answers[0].SelectedItem,(string)answers[1].SelectedItem,(string)answers[2].SelectedItem,(string)answers[3].SelectedItem,evidence.Text);if(proposalRevision.HasValue)service.RecordProposalConflictReview(project,proposalRevision.Value,expected,input,confirm.IsChecked==true);else service.RecordConflictReview(project,expected,input,confirm.IsChecked==true);Refresh();}
}
