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
 readonly CheckBox current=new(){Content="Current versions only",IsChecked=true};
 readonly TextBox search=new(){MaxLength=180,ToolTip="Search file names"};
 readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap},details=new(){TextWrapping=TextWrapping.Wrap};
 readonly Button add=new(){Content="Add document"},version=new(){Content="Add new version"},export=new(){Content="Save copy"},folders=new(){Content="Project folders"};
 IReadOnlyList<ProjectDocument> rows=Array.Empty<ProjectDocument>();
 bool loaded;
 public ProjectDocumentsWindow(LocalPractice service,long project)
 {
  this.service=service;this.project=project;Title="Project documents";Width=1000;Height=620;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(16)};var head=new StackPanel();
  head.Children.Add(new TextBlock{Text=Title,FontSize=24,FontWeight=FontWeights.SemiBold});
  head.Children.Add(new TextBlock{Text="Stored document copies (up to 20 MiB). Earlier versions are retained. Files are never opened automatically.",TextWrapping=TextWrapping.Wrap});
  head.Children.Add(new TextBlock{Text="Search file names"});head.Children.Add(search);
  var bar=new WrapPanel();bar.Children.Add(current);
  foreach(var b in new[]{add,version,export,folders}){b.Margin=new Thickness(2);bar.Children.Add(b);}
  WorkWindowFields.Button(bar,"Refresh",()=>Run(Refresh));head.Children.Add(bar);head.Children.Add(status);
  DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
  DockPanel.SetDock(details,Dock.Bottom);details.Margin=new Thickness(0,8,0,0);root.Children.Add(details);
  WorkWindowFields.Columns(grid,("File name","FileName"),("Category","Category"),("Access","Classification"),("Bytes","Bytes"),("Current","IsCurrent"),("Registered UTC","RecordedUtc"));
  root.Children.Add(grid);Content=root;
  add.Click+=(_,_)=>Run(()=>Register(null));version.Click+=(_,_)=>Run(()=>Register(Selected()));
  export.Click+=(_,_)=>Run(Export);folders.Click+=(_,_)=>Run(()=>new ProjectFolderPreviewWindow(service,project){Owner=this}.ShowDialog());
  current.Checked+=(_,_)=>Run(Refresh);current.Unchecked+=(_,_)=>Run(Refresh);
  search.TextChanged+=(_,_)=>Run(Filter);grid.SelectionChanged+=(_,_)=>UpdateActions();
  Closed+=(_,_)=>Clear();Run(Refresh);
 }
 ProjectDocument Selected()=>grid.SelectedItem as ProjectDocument??throw new ArgumentException("Select a document.");
 void Clear(){loaded=false;rows=Array.Empty<ProjectDocument>();grid.ItemsSource=null;details.Text="";add.IsEnabled=version.IsEnabled=export.IsEnabled=folders.IsEnabled=false;}
 void Run(Action a){try{a();}catch(UnauthorizedAccessException){Clear();status.Text="Access denied.";}catch(ArgumentException e){status.Text=e.Message;}catch(Exception){Clear();status.Text="Operation failed; refresh before retrying.";}}
 void Refresh(){Clear();rows=service.ReadProjectDocuments(project,current.IsChecked==true);loaded=true;Filter();}
 void Filter()
 {
  if(!loaded)return;
  // Reacquire authorized rows before presenting a new filtered view. A revoked
  // restricted-document grant must not reveal cached names through search.
  rows=service.ReadProjectDocuments(project,current.IsChecked==true);
  var matching=rows.Where(x=>x.FileName.Contains(search.Text.Trim(),StringComparison.OrdinalIgnoreCase)).ToArray();
  grid.ItemsSource=matching;status.Text=matching.Length+" document(s)";UpdateActions();
 }
 void UpdateActions()
 {
  var selected=grid.SelectedItem as ProjectDocument;
  bool view=loaded&&service.Can("Documents.View")&&service.Can("Projects.View")&&service.Can("Clients.View");
  bool visible=view&&selected!=null&&(selected.Classification!="Restricted"||service.Can("Documents.Restricted"));
  add.IsEnabled=view&&service.Can("Documents.Record");version.IsEnabled=add.IsEnabled&&visible&&selected!.IsCurrent;
  export.IsEnabled=visible;folders.IsEnabled=view;
  details.Text=visible?"Selected: "+selected!.FileName+"\nSHA-256: "+selected.Hash:"Select a document to view its identity.";
 }
 void Register(ProjectDocument? prior)
 {
  if(!service.Can("Documents.Record"))throw new UnauthorizedAccessException();
  if(prior!=null&&!prior.IsCurrent)throw new ArgumentException("Select the current version.");
  if(prior?.Classification=="Restricted"&&!service.Can("Documents.Restricted"))throw new UnauthorizedAccessException();
  var picker=new OpenFileDialog{Title=prior==null?"Choose document":"Choose replacement version",Filter="Supported documents|*.pdf;*.dwg;*.dxf;*.rvt;*.docx;*.xlsx;*.txt",CheckFileExists=true};
  if(picker.ShowDialog(this)!=true)return;
  var w=new Window{Owner=this,Title=prior==null?"Add document":"Add new version",Width=500,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner};var p=new StackPanel{Margin=new Thickness(16)};
  p.Children.Add(new TextBlock{Text=Path.GetFileName(picker.FileName),FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
  if(prior!=null)p.Children.Add(new TextBlock{Text="Replaces: "+prior.FileName+". Previous version remains in history.",TextWrapping=TextWrapping.Wrap});
  var classification=new ComboBox{ItemsSource=new[]{"Production","Restricted"}.Where(x=>x=="Production"||service.Can("Documents.Restricted")).ToArray(),SelectedItem=prior?.Classification??"Production",IsEnabled=prior==null};
  p.Children.Add(new TextBlock{Text="Access (choose Restricted for confidential records)"});p.Children.Add(classification);
  var category=new ComboBox{ItemsSource=LocalPractice.DocumentCategories,SelectedItem=prior?.Category,IsEnabled=prior==null};p.Children.Add(new TextBlock{Text="Category"});p.Children.Add(category);
  var b=new Button{Content="Save document",IsDefault=true};var cancel=new Button{Content="Cancel",IsCancel=true};var bar=new WrapPanel();bar.Children.Add(b);bar.Children.Add(cancel);p.Children.Add(bar);var key=Guid.NewGuid();w.Content=p;
  b.Click+=(_,_)=>{try{
   if(classification.SelectedItem is not string c||category.SelectedItem is not string g)throw new ArgumentException("Choose access and category.");
   if(g=="Contracts"&&c=="Production"&&MessageBox.Show(w,"Contracts may contain confidential terms. Save as Production, visible to ordinary document viewers?","Confirm document access",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
   using var stream=new FileStream(picker.FileName,FileMode.Open,FileAccess.Read,FileShare.Read);
   if(stream.Length is <1 or >20971520)throw new ArgumentException("Document exceeds size limits.");
   var bytes=new byte[(int)stream.Length];stream.ReadExactly(bytes);if(stream.ReadByte()!=-1)throw new ArgumentException("File changed.");
   service.RegisterProjectDocument(project,key,prior?.Id,c,g,Path.GetFileName(picker.FileName),bytes);w.DialogResult=true;
  }catch(ArgumentException e){MessageBox.Show(w,e.Message);}catch(UnauthorizedAccessException){MessageBox.Show(w,"Access denied.");w.DialogResult=false;}catch(Exception){MessageBox.Show(w,"Save failed. Check document history before retrying.");}};
  w.ShowDialog();Refresh();
 }
 void Export(){var d=Selected();var picker=new SaveFileDialog{FileName=d.FileName,DefaultExt=Path.GetExtension(d.FileName),OverwritePrompt=true};if(picker.ShowDialog(this)!=true)return;var content=service.RetrieveProjectDocument(project,d.Id,d.Hash);if(!string.Equals(Path.GetExtension(picker.FileName),Path.GetExtension(d.FileName),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Keep the original document extension.");using var stream=new FileStream(picker.FileName,FileMode.CreateNew,FileAccess.Write,FileShare.None);stream.Write(content.Bytes);stream.Flush(true);status.Text="Verified copy saved. Scan before opening. Existing files are never overwritten.";}
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
