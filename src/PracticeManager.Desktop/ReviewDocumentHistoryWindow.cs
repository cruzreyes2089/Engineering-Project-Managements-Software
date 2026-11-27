using System.Windows;
using System.Windows.Controls;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class ReviewDocumentHistoryWindow:Window
{
 readonly Func<IReadOnlyList<GeneratedReviewMetadata>> list;readonly Func<GeneratedReviewMetadata,GeneratedReviewDocument> retrieve;
 readonly DataGrid grid=WorkWindowFields.Grid();readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 public ReviewDocumentHistoryWindow(Func<IReadOnlyList<GeneratedReviewMetadata>> list,Func<GeneratedReviewMetadata,GeneratedReviewDocument> retrieve)
 {
  this.list=list;this.retrieve=retrieve;Title="Registered review document history";Width=1020;Height=550;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();head.Children.Add(new TextBlock{Text="Review copies — historical evidence",FontSize=24,FontWeight=FontWeights.SemiBold});head.Children.Add(new TextBlock{Text="Older copies may no longer match the current proposal revision or screening. All copies are internal only, never evidence of issue, delivery or approval.",TextWrapping=TextWrapping.Wrap});var bar=new WrapPanel();WorkWindowFields.Button(bar,"Refresh",()=>Run(Refresh));WorkWindowFields.Button(bar,"Save verified historical copy",()=>Run(()=>{if(grid.SelectedItem is not GeneratedReviewMetadata row)throw new ArgumentException("Select a review document.");ReviewDocumentSave.Save(this,row.FileName,()=>retrieve(row));}));head.Children.Add(bar);head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  WorkWindowFields.Columns(grid,("File","FileName"),("Source SHA-256","SourceHash"),("Document SHA-256","Sha256"),("Bytes","Bytes"),("Registered UTC","CreatedUtc"));root.Children.Add(grid);Content=root;Run(Refresh);
 }
 void Refresh(){grid.ItemsSource=null;grid.ItemsSource=list();status.Text="";}
 void Run(Action a){try{a();}catch(UnauthorizedAccessException){grid.ItemsSource=null;status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Review history unavailable or file save failed.";}}
}
