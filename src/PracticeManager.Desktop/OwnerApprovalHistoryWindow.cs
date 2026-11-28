using System.Windows;
using System.Windows.Controls;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class OwnerApprovalHistoryWindow:Window
{
 readonly LocalPractice service;readonly DataGrid policy=WorkWindowFields.Grid(),decisions=WorkWindowFields.Grid();readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 public OwnerApprovalHistoryWindow(LocalPractice service)
 {
  this.service=service;Title="Owner approval audit — read only";Width=1160;Height=660;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();head.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});head.Children.Add(new TextBlock{Text="Policy events and self-decisions are retained separately. An unmarked historical self-decision is not evidence of authorization under the new policy.",TextWrapping=TextWrapping.Wrap});var refresh=new Button{Content="Refresh"};refresh.Click+=(_,_)=>Refresh();head.Children.Add(refresh);head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  WorkWindowFields.Columns(policy,("Policy event","Id"),("Owner","Owner"),("Change","Change"),("Reason","Reason"),("Actor","Actor"),("UTC","RecordedUtc"));
  WorkWindowFields.Columns(decisions,("Kind","Kind"),("Record ID","RecordId"),("Decision","Decision"),("Actor","Actor"),("Justification","Justification"),("UTC","RecordedUtc"),("Policy event","PolicyEventId"),("Evidence","EvidenceStatus"));
  var tabs=new TabControl();tabs.Items.Add(new TabItem{Header="Policy history",Content=policy});tabs.Items.Add(new TabItem{Header="Self-decision evidence",Content=decisions});root.Children.Add(tabs);Content=root;Refresh();
 }
 void Refresh()
 {
  policy.ItemsSource=decisions.ItemsSource=null;status.Text="";
  try{policy.ItemsSource=service.ReadOwnerPolicyHistory();decisions.ItemsSource=service.ReadOwnerDecisionEvidence();}
  catch(UnauthorizedAccessException){policy.ItemsSource=decisions.ItemsSource=null;status.Text="Access denied.";}
  catch(ArgumentException e){status.Text=e.Message;}
  catch(Exception){policy.ItemsSource=decisions.ItemsSource=null;status.Text="Audit history unavailable.";}
 }
}
