using System.Windows;
using System.Windows.Controls;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class ReviewDocumentHistoryWindow:Window
{
 readonly Func<IReadOnlyList<GeneratedReviewMetadata>> list;readonly Func<GeneratedReviewMetadata,GeneratedReviewDocument> retrieve;
 readonly Button save=new(){Content="Save copy",IsEnabled=false};
 readonly DataGrid grid=WorkWindowFields.Grid();readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 public ReviewDocumentHistoryWindow(Func<IReadOnlyList<GeneratedReviewMetadata>> list,Func<GeneratedReviewMetadata,GeneratedReviewDocument> retrieve)
 {
  this.list=list;this.retrieve=retrieve;Title="Registered review document history";Width=1020;Height=550;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();head.Children.Add(new TextBlock{Text="Review copies — historical evidence",FontSize=24,FontWeight=FontWeights.SemiBold});head.Children.Add(new TextBlock{Text="Previous review copies may be outdated. Internal use only.",TextWrapping=TextWrapping.Wrap});var bar=new WrapPanel();WorkWindowFields.Button(bar,"Refresh",()=>Run(Refresh));save.Click+=(_,_)=>Run(()=>{if(grid.SelectedItem is not GeneratedReviewMetadata row)throw new ArgumentException("Select a review document.");ReviewDocumentSave.Save(this,row.FileName,()=>retrieve(row));});bar.Children.Add(save);var technical=new CheckBox{Content="Show verification details",Margin=new Thickness(4)};bar.Children.Add(technical);head.Children.Add(bar);head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  WorkWindowFields.Columns(grid,("File","FileName"),("Source SHA-256","SourceHash"),("Document SHA-256","Sha256"),("Bytes","Bytes"),("Registered UTC","CreatedUtc"));var details=new[]{grid.Columns[1],grid.Columns[2],grid.Columns[3]};void ShowDetails(bool show){foreach(var column in details)column.Visibility=show?Visibility.Visible:Visibility.Collapsed;}ShowDetails(false);technical.Checked+=(_,_)=>ShowDetails(true);technical.Unchecked+=(_,_)=>ShowDetails(false);root.Children.Add(grid);Content=root;grid.SelectionChanged+=(_,_)=>save.IsEnabled=grid.SelectedItem is GeneratedReviewMetadata;Closed+=(_,_)=>{grid.ItemsSource=null;status.Text="";save.IsEnabled=false;};Run(Refresh);
 }
 void Refresh(){grid.ItemsSource=null;var rows=list();grid.ItemsSource=rows;status.Text=$"{rows.Count} registered review copies. Select one to save its exact bytes.";}
 void Run(Action a){try{a();}catch(UnauthorizedAccessException){grid.ItemsSource=null;status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){grid.ItemsSource=null;status.Text="Review history unavailable or file save failed.";}}
}
