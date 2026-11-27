using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PracticeManager.Core;
using PracticeManager.Persistence;
namespace PracticeManager.Desktop;
internal sealed class ProjectDocumentsWindow:Window
{
 readonly LocalPractice service;readonly long project;readonly DataGrid grid=WorkWindowFields.Grid();
 readonly CheckBox current=new(){Content="Current versions only",IsChecked=true};readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 public ProjectDocumentsWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="Project documents";Width=1060;Height=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();head.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});head.Children.Add(new TextBlock{Text="Stored immutable copies, 20 MiB maximum per file. Restricted records require separate permission. No document is opened or executed automatically.",TextWrapping=TextWrapping.Wrap});var bar=new WrapPanel();bar.Children.Add(current);WorkWindowFields.Button(bar,"Refresh",()=>Run(Refresh));WorkWindowFields.Button(bar,"Folder template preview",()=>Run(()=>new ProjectFolderTemplateWindow(){Owner=this}.ShowDialog()));WorkWindowFields.Button(bar,"Register document",()=>Run(()=>Register(null)));WorkWindowFields.Button(bar,"Register new version",()=>Run(()=>Register(Selected())));WorkWindowFields.Button(bar,"Save verified copy",()=>Run(Export));if(service.Can("Documents.Record")&&service.Can("Administration.Manage"))WorkWindowFields.Button(bar,"Prepare PDF release review",()=>Run(()=>new ProjectReleaseReviewWindow(service,project){Owner=this}.ShowDialog()));if(service.IsDocumentAcceptance&&service.Can("Administration.Manage")){WorkWindowFields.Button(bar,"Folder configuration / setup",()=>Run(()=>new DocumentFolderAcceptanceWindow(service,project){Owner=this}.ShowDialog()));WorkWindowFields.Button(bar,"Transmittal center",()=>Run(()=>new TransmittalAcceptanceWindow(service,project){Owner=this}.ShowDialog()));}head.Children.Add(bar);head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  WorkWindowFields.Columns(grid,("File name","FileName"),("Classification","Classification"),("Category","Category"),("Bytes","Bytes"),("Current","IsCurrent"),("Registered UTC","RecordedUtc"),("SHA-256","Hash"));root.Children.Add(grid);Content=root;Run(Refresh);
 }
 ProjectDocument Selected()=>grid.SelectedItem as ProjectDocument??throw new ArgumentException("Select a document.");
 void Run(Action a){try{a();}catch(UnauthorizedAccessException){grid.ItemsSource=null;status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Operation failed; no successful change assumed.";}
 }
 void Refresh(){grid.ItemsSource=null;grid.ItemsSource=service.ReadProjectDocuments(project,current.IsChecked==true);status.Text="";}
 void Register(ProjectDocument? prior)
 {
  if(prior!=null&&!prior.IsCurrent)throw new ArgumentException("Select current version.");
  var w=new Window{Owner=this,Title="Register immutable document",Width=470,Height=340,WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new Thickness(16)};
  var classification=new ComboBox{ItemsSource=new[]{"Production","Restricted"}.Where(x=>x=="Production"||service.Can("Documents.Restricted")).ToArray(),SelectedItem=prior?.Classification??"Production",IsEnabled=prior==null};p.Children.Add(new TextBlock{Text="Classification"});p.Children.Add(classification);
  var category=new ComboBox{ItemsSource=LocalPractice.DocumentCategories,SelectedItem=prior?.Category,IsEnabled=prior==null};p.Children.Add(new TextBlock{Text="Category"});p.Children.Add(category);var b=new Button{Content="Select document and register"};p.Children.Add(b);var key=Guid.NewGuid();w.Content=p;
  b.Click+=(_,_)=>{try{if(classification.SelectedItem is not string c||category.SelectedItem is not string g)throw new ArgumentException("Select metadata.");var picker=new OpenFileDialog{Filter="Supported documents|*.pdf;*.dwg;*.dxf;*.rvt;*.docx;*.xlsx;*.txt",CheckFileExists=true};if(picker.ShowDialog(w)!=true)return;var source=LocalStoragePath.FilePath(picker.FileName,mustExist:true);using var stream=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read);if(stream.Length is <1 or >20971520)throw new ArgumentException("Document exceeds size limits.");var bytes=new byte[(int)stream.Length];stream.ReadExactly(bytes);if(stream.ReadByte()!=-1)throw new ArgumentException("File changed.");service.RegisterProjectDocument(project,key,prior?.Id,c,g,Path.GetFileName(picker.FileName),bytes);w.DialogResult=true;}catch(ArgumentException e){MessageBox.Show(w,e.Message);}catch(Exception){MessageBox.Show(w,"Registration failed. Check history before retrying.");}};if(w.ShowDialog()==true)Refresh();
 }
 void Export(){var d=Selected();var picker=new SaveFileDialog{FileName=d.FileName,DefaultExt=Path.GetExtension(d.FileName),OverwritePrompt=true};if(picker.ShowDialog(this)!=true)return;var content=service.RetrieveProjectDocument(project,d.Id,d.Hash);if(!string.Equals(Path.GetExtension(picker.FileName),Path.GetExtension(d.FileName),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Keep the original document extension.");VerifiedDocumentFile.Save(picker.FileName,Path.GetExtension(d.FileName).ToLowerInvariant(),content.Bytes,d.Hash);status.Text="Verified private copy saved. Scan before opening. Existing files are never overwritten.";}
}
internal sealed class ProjectQualityWindow:Window
{
 readonly LocalPractice service;readonly long project;readonly DataGrid quality=WorkWindowFields.Grid(),scope=WorkWindowFields.Grid();readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
 public ProjectQualityWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="Project QA/QC and additional scope";Width=1050;Height=620;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();head.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});head.Children.Add(new TextBlock{Text="Append-only quality history; separate scope authorization record. Do not treat a proposed scope change as approved work.",TextWrapping=TextWrapping.Wrap});head.Children.Add(status);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  var tabs=new TabControl();DockPanel Page(DataGrid grid,params(string Label,Action Action)[] buttons){var p=new DockPanel();var bar=new WrapPanel();foreach(var (label,action) in buttons)WorkWindowFields.Button(bar,label,()=>Run(action));DockPanel.SetDock(bar,Dock.Top);p.Children.Add(bar);p.Children.Add(grid);return p;}
  WorkWindowFields.Columns(quality,("Title","Title"),("Severity","Severity"),("State","State"),("Description","Description"),("Resolution","Resolution"));WorkWindowFields.Columns(scope,("Title","Title"),("Scope","Scope"),("Decision","State"),("Evidence","DecisionEvidence"));
  tabs.Items.Add(new TabItem{Header="Quality items",Content=Page(quality,("New",()=>Create(false)),("Add note",AddNote),("View notes",ViewNotes),("Close",CloseItem),("Refresh",Refresh))});tabs.Items.Add(new TabItem{Header="Additional scope",Content=Page(scope,("Propose",()=>Create(true)),("Authorize",()=>Decision(true)),("Reject",()=>Decision(false)),("Refresh",Refresh))});root.Children.Add(tabs);Content=root;Run(Refresh);
 }
 void Run(Action a){try{a();}catch(UnauthorizedAccessException){quality.ItemsSource=scope.ItemsSource=null;status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){status.Text="Operation failed; refresh before retrying.";}}
 void Refresh(){quality.ItemsSource=scope.ItemsSource=null;quality.ItemsSource=service.ReadQualityItems(project);scope.ItemsSource=service.ReadScopeChanges(project);status.Text="";}
 QualityItem Selected()=>quality.SelectedItem as QualityItem??throw new ArgumentException("Select a quality item.");
 void Create(bool change)
 {
  var w=new Window{Owner=this,Title=change?"Propose additional scope":"Record quality item",Width=560,Height=460,WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new Thickness(16)};
  var title=new TextBox{MaxLength=200};p.Children.Add(new TextBlock{Text="Title"});p.Children.Add(title);var severity=new ComboBox{ItemsSource=new[]{"Observation","Correction","Critical"},SelectedIndex=0};if(!change){p.Children.Add(new TextBlock{Text="Severity"});p.Children.Add(severity);}var text=new TextBox{MaxLength=4000,AcceptsReturn=true,Height=180,TextWrapping=TextWrapping.Wrap};p.Children.Add(new TextBlock{Text=change?"Additional scope":"Description"});p.Children.Add(text);var b=new Button{Content="Record"};p.Children.Add(b);w.Content=p;b.Click+=(_,_)=>{try{if(change)service.ProposeScopeChange(project,title.Text,text.Text);else service.CreateQualityItem(project,title.Text,(string)severity.SelectedItem,text.Text);w.DialogResult=true;}catch(ArgumentException e){MessageBox.Show(w,e.Message);}catch(Exception){MessageBox.Show(w,"Save failed.");}};if(w.ShowDialog()==true)Refresh();
 }
 void AddNote(){var item=Selected();var note=WorkWindowFields.Note(this,"Quality note");if(note!=null){service.AddQualityNote(project,item.Id,note);Refresh();}}
 void ViewNotes(){var item=Selected();var notes=service.ReadQualityNotes(project,item.Id);var w=new Window{Owner=this,Title="Quality history",Width=640,Height=420,Content=new TextBox{Text=string.Join("\n\n",notes.Select(x=>x.RecordedUtc+"\n"+x.Note)),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};w.ShowDialog();}
 void CloseItem(){var item=Selected();var n=WorkWindowFields.Note(this,"Close with resolution evidence");if(n!=null){var result=service.CloseQualityItem(project,item.Id,n);Refresh();status.Text=result;}}
 void Decision(bool yes){if(scope.SelectedItem is not ScopeChange x)throw new ArgumentException("Select additional scope.");if(yes&&MessageBox.Show(this,"Confirm you independently reviewed written client authorization for this additional scope.","Scope authorization",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;var n=WorkWindowFields.Note(this,"Scope decision evidence");if(n!=null){var result=service.DecideScopeChange(project,x.Id,yes,n,yes);Refresh();status.Text=result;}}
}
