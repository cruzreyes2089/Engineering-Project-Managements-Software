// Staged WPF source; not compiled or connected to application navigation.
// Enable only after the separately numbered migration and service pass qualification.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using PracticeManager.Persistence;

namespace PracticeManager.Desktop;

internal sealed class HistoricalAssociationWindow:Window
{
    private readonly LocalPractice service;
    private readonly string legacyId;
    private readonly TextBox number=new(){MaxLength=100,MinWidth=180};
    private readonly ComboBox choices=new(){MinWidth=320,DisplayMemberPath="Label"};
    private readonly TextBox evidence=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,
        VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=110};
    private readonly TextBox note=new(){MaxLength=1000,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=70};
    private readonly CheckBox confirmed=new(){Content="I independently reviewed the evidence and confirm this action."};
    private readonly Button commit=new(){Content="Record association",IsEnabled=false};
    private readonly Button reverse=new(){Content="Review reversal",IsEnabled=false};
    private readonly DataGrid history=new(){IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,
        CanUserDeleteRows=false,SelectionMode=DataGridSelectionMode.Single};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly DispatcherTimer guard=new(){Interval=TimeSpan.FromSeconds(20)};
    private HistoricalAssociationCommitReview? linkReview;
    private HistoricalAssociationReversalReview? reversalReview;

    public HistoricalAssociationWindow(LocalPractice service,string legacyId,bool syntheticAcceptance)
    {
        if(!syntheticAcceptance||!service.CanReviewHistoricalAssociations())throw new UnauthorizedAccessException();
        this.service=service;this.legacyId=legacyId;
        Title="Historical association review — synthetic only";Width=1040;Height=760;MinWidth=800;MinHeight=650;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new Thickness(20)};
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text="Historical association review",FontSize=24,FontWeight=FontWeights.SemiBold});
        top.Children.Add(new TextBlock{Text="Historical ID: "+legacyId+" · Association is not authorization to perform work, bill or clear conflicts.",TextWrapping=TextWrapping.Wrap});
        var search=new WrapPanel{Margin=new Thickness(0,8,0,8)};
        search.Children.Add(new TextBlock{Text="Exact native project number",VerticalAlignment=VerticalAlignment.Center});
        search.Children.Add(number);
        var find=new Button{Content="Find"};find.Click+=(_,_)=>Run(Find);search.Children.Add(find);
        search.Children.Add(choices);
        var preview=new Button{Content="Review association"};preview.Click+=(_,_)=>Run(Preview);search.Children.Add(preview);
        top.Children.Add(search);top.Children.Add(evidence);
        top.Children.Add(new TextBlock{Text="Required review evidence / reason"});
        top.Children.Add(note);top.Children.Add(confirmed);
        var actions=new WrapPanel();
        commit.Click+=(_,_)=>Run(Commit);actions.Children.Add(commit);
        reverse.Click+=(_,_)=>Run(PreviewReversal);actions.Children.Add(reverse);
        var refresh=new Button{Content="Refresh history"};refresh.Click+=(_,_)=>Run(Refresh);actions.Children.Add(refresh);
        top.Children.Add(actions);top.Children.Add(status);
        Add("Association","Id",100);Add("Native project","NativeProjectNumber",130);
        Add("Active","IsActive",70);Add("Recorded UTC","RecordedUtc",150);
        Add("Review reason","ReviewNote",200);Add("Reversed UTC","ReversedUtc",150);Add("Reversal reason","ReversalNote",200);
        root.Children.Add(history);Content=root;
        number.TextChanged+=(_,_)=>{choices.ItemsSource=null;InvalidateReview();};
        choices.SelectionChanged+=(_,_)=>InvalidateReview();
        history.SelectionChanged+=(_,_)=>{InvalidateReview();reverse.IsEnabled=history.SelectedItem is HistoricalAssociationHistoryItem x&&x.IsActive;};
        confirmed.Checked+=(_,_)=>EnableCommit();
        confirmed.Unchecked+=(_,_)=>EnableCommit();
        note.TextChanged+=(_,_)=>EnableCommit();
        Loaded+=(_,_)=>{Run(Refresh);guard.Start();};
        Activated+=(_,_)=>VerifyAccess();
        guard.Tick+=(_,_)=>VerifyAccess();
        Closed+=(_,_)=>{guard.Stop();Clear();};
    }

    private void Add(string header,string binding,double width)
        =>history.Columns.Add(new DataGridTextColumn{Header=header,Binding=new Binding(binding),Width=width});

    private void EnableCommit()=>commit.IsEnabled=(linkReview!=null||reversalReview!=null)
        &&confirmed.IsChecked==true&&!string.IsNullOrWhiteSpace(note.Text);

    private void InvalidateReview()
    {
        linkReview=null;reversalReview=null;evidence.Clear();note.Clear();confirmed.IsChecked=false;
        commit.Content="Record association";commit.IsEnabled=false;
    }

    private void Clear()
    {
        InvalidateReview();choices.ItemsSource=null;history.ItemsSource=null;reverse.IsEnabled=false;
    }

    private void VerifyAccess()
    {
        try{if(service.CanReviewHistoricalAssociations())return;}catch(Exception){}
        Clear();guard.Stop();Close();
    }

    private void Run(Action action)
    {
        try
        {
            if(!service.CanReviewHistoricalAssociations())throw new UnauthorizedAccessException();
            action();
        }
        catch(UnauthorizedAccessException){Clear();guard.Stop();Close();}
        catch(Exception){Clear();status.Text="Review unavailable or changed. No success is assumed; refresh and review again.";}
    }

    private void Refresh()
    {
        InvalidateReview();choices.ItemsSource=null;history.ItemsSource=null;
        history.ItemsSource=service.ReadHistoricalAssociationHistory(legacyId);
        reverse.IsEnabled=false;status.Text="History refreshed. Review a new association or select an active record for reversal.";
    }

    private void Find()
    {
        InvalidateReview();choices.ItemsSource=null;
        var found=service.FindHistoricalAssociationProjects(legacyId,number.Text);
        choices.ItemsSource=found;
        status.Text=found.Count==0?"No exact same-client accepted project found.":"Select the exact project and review evidence.";
    }

    private void Preview()
    {
        InvalidateReview();
        if(choices.SelectedItem is not HistoricalAssociationProjectChoice p)throw new ArgumentException("Select a project.");
        var review=service.PrepareHistoricalAssociation(legacyId,p.ProjectId);
        linkReview=review;
        evidence.Text=$"Historical ID: {review.LegacyId}\nNative project: {review.NativeProjectNumber}\nTitle: {(review.TitleMatches?"Exact match":"Different")}\nSite: {(!review.SiteAvailable?"Unavailable":review.SiteMatches?"Exact match":"Different")}\nArchive hash: {review.ArchiveTransferHash}\nAccepted proposal hash: {review.ProposalHash}\nWork authorized: No";
        status.Text="A title or site match is not proof of identity. Independently review before confirming.";
        EnableCommit();
    }

    private void PreviewReversal()
    {
        InvalidateReview();
        if(history.SelectedItem is not HistoricalAssociationHistoryItem x||!x.IsActive)throw new ArgumentException("Select an active association.");
        reversalReview=service.PrepareHistoricalAssociationReversal(x.Id);
        evidence.Text=$"Reverse association {x.Id}: {x.LegacyId} / {x.NativeProjectNumber}\nOriginal association remains in history. No project or authorization changes.";
        commit.Content="Record reversal";EnableCommit();
    }

    private void Commit()
    {
        if(confirmed.IsChecked!=true||string.IsNullOrWhiteSpace(note.Text))throw new ArgumentException("Explicit review required.");
        var link=linkReview;var reversal=reversalReview;var reason=note.Text;
        if((link==null)==(reversal==null))throw new ArgumentException("A single fresh review is required.");
        if(MessageBox.Show(this,"Record this reviewed historical action? No operational authority is granted.",
            "Confirm historical review",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        // The modal confirmation can pump messages. Recheck review identity and permissions.
        if(linkReview!=link||reversalReview!=reversal||confirmed.IsChecked!=true||note.Text!=reason)
            throw new ArgumentException("Review changed.");
        if(link!=null)service.RecordHistoricalAssociation(link,true,reason);
        else service.ReverseHistoricalAssociation(reversal!,true,reason);
        Refresh();status.Text="Historical action recorded. Operational authorization unchanged.";
    }
}
