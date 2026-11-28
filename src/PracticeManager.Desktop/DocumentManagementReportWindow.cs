using System.Windows;
using System.Windows.Controls;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class DocumentManagementReportWindow:Window
{
 readonly LocalPractice service;readonly long project;
 readonly TextBlock result=new(){TextWrapping=TextWrapping.Wrap};
 public DocumentManagementReportWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="SYNTHETIC — Document register report";Width=750;Height=620;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var p=new StackPanel{Margin=new Thickness(16)};
  var bar=new WrapPanel();WorkWindowFields.Button(bar,"Refresh all-date report",Refresh);p.Children.Add(bar);p.Children.Add(result);Content=new ScrollViewer{Content=p};
  Closed+=(_,_)=>result.Text="";Refresh();
 }
 void Refresh()
 {
  result.Text="";
  try
  {
   var r=service.ReadAcceptanceDocumentManagementReport(project);
   result.Text=$"{r.ProjectNumber} • All dates\n\nProduction document versions: {r.ProductionVersions}\nCurrent production versions: {r.CurrentProductionVersions}\nCurrent production PDFs: {r.CurrentProductionPdfs}\n\nRegistered folder reference: {(r.HasRegisteredFolder?"Yes":"No")}\nReference uses current configuration: {(r.FolderUsesCurrentConfiguration?"Yes":"No")}\n\nLatest transmittal states (mutually exclusive):\nPrepared: {r.Preparations}\nIssue recorded: {r.Issues}\nDelivery recorded: {r.Deliveries}\nWithdrawn: {r.Withdrawals}\n\nHistorical cumulative counts (may include withdrawals):\nEver issued: {r.EverIssued}\nEver delivered: {r.EverDelivered}\nPrepared with superseded documents: {r.SupersededPreparations}\nOpen QA items: {r.OpenQualityItems}\n\nRestricted documents excluded. Folder metadata is not proof of current external directory contents. Recorded events are synthetic attestations, not independently verified delivery or authorization. This report does not authorize issuance or closeout.";
  }
  catch(UnauthorizedAccessException){result.Text="Access denied.";}
  catch(ArgumentException e){result.Text=e.Message;}
  catch(Exception){result.Text="Document register report unavailable.";}
 }
}
