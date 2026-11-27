using System.Windows;
using System.Windows.Controls;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class InvoiceCandidateWindow:Window
{
 readonly LocalPractice service;readonly long project;readonly InvoiceDraftSummary draft;
 readonly TextBox reference=new(){MaxLength=180};
 readonly CheckBox currency=new(){Content="I verified the synthetic Manager business uses USD only."};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 readonly Button run=new(){Content="Compare current observed invoice"};
 CancellationTokenSource? pending;bool closed;
 public InvoiceCandidateWindow(LocalPractice service,long project,InvoiceDraftSummary draft)
 {
  this.service=service;this.project=project;this.draft=draft;Title="Synthetic invoice candidate comparison";Width=680;Height=430;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var p=new StackPanel{Margin=new Thickness(18)};p.Children.Add(new TextBlock{Text=draft.Number+" — "+draft.State+" — "+draft.Total.ToString("0.00")+" USD",FontSize=22});
  p.Children.Add(new TextBlock{Text="Read-only comparison requires the approved draft, current provisional customer link, exact external reference, issue date and total. A match is not proof of line allocation, tax, due date, legal issuance or receipt. No link or accounting transaction is created.",TextWrapping=TextWrapping.Wrap});
  p.Children.Add(new TextBlock{Text="Exact external invoice reference"});p.Children.Add(reference);p.Children.Add(currency);p.Children.Add(run);p.Children.Add(status);Content=p;
  run.Click+=async(_,_)=>await Compare();Closed+=(_,_)=>{closed=true;pending?.Cancel();};
  reference.TextChanged+=(_,_)=>Invalidate();currency.Unchecked+=(_,_)=>Invalidate();
 }
 void Invalidate(){pending?.Cancel();status.Text="";}
 async Task Compare()
 {
  if(pending!=null)return;using var ct=new CancellationTokenSource();pending=ct;run.IsEnabled=false;status.Text="Reading synthetic evidence...";
  try{
   var r=await service.CompareSavedInvoiceCandidateAsync(project,draft.Id,draft.PayloadHash,reference.Text,currency.IsChecked==true,ct.Token);
   if(closed||ct.IsCancellationRequested)return;
   status.Text=r.Outcome+(r.ObservedTotal.HasValue?$"\nObserved amount USD: {r.ObservedTotal:0.00}\nObserved outstanding USD: {r.Outstanding:0.00}\nObserved UTC: {r.ObservedUtc:u}\nManual review only.":"");
  }catch(UnauthorizedAccessException){if(!closed)status.Text="Access denied.";}catch(ArgumentException e){if(!closed)status.Text=e.Message;}catch(Exception){if(!closed)status.Text="Comparison unavailable. No match assumed.";}
  finally{pending=null;if(!closed)run.IsEnabled=true;}
 }
}
