using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using PracticeManager.Persistence;

namespace PracticeManager.Desktop;

internal sealed class HistoricalProjectWindow:Window
{
    private sealed record ClientFilter(string Label,string? LegacyId);
    private readonly LocalPractice service;
    private readonly bool syntheticAcceptance;
    private readonly TextBox search=new(){MaxLength=150,MinWidth=160};
    private readonly ComboBox client=new(){MinWidth=160,DisplayMemberPath="Label",Margin=new Thickness(8,3,8,8)};
    private readonly DataGrid grid=new(){AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,
        CanUserDeleteRows=false,SelectionMode=DataGridSelectionMode.Single,HeadersVisibility=DataGridHeadersVisibility.Column};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly TextBox detail=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,
        VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=110};
    private readonly Button import=new(){Content="Review synthetic archive import",IsEnabled=false};
    private readonly DispatcherTimer guard=new(){Interval=TimeSpan.FromSeconds(20)};
    private bool mayImport;

    public HistoricalProjectWindow(LocalPractice service,bool syntheticAcceptance)
    {
        this.service=service;this.syntheticAcceptance=syntheticAcceptance;
        Title="Historical Project Register — archive only";Width=1060;Height=710;MinWidth=760;MinHeight=550;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel{Margin=new Thickness(20)};
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text="Historical Project Register",FontSize=26,FontWeight=FontWeights.SemiBold});
        top.Children.Add(new TextBlock{Text="Source-reported history only. No current authorization, conflict clearance, native project number or billing authority.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,12)});
        var filters=new WrapPanel();filters.Children.Add(new TextBlock{Text="Search",VerticalAlignment=VerticalAlignment.Center});
        filters.Children.Add(search);filters.Children.Add(new TextBlock{Text="Legacy client",VerticalAlignment=VerticalAlignment.Center});
        filters.Children.Add(client);
        var refresh=new Button{Content="Search / refresh"};refresh.Click+=(_,_)=>Refresh();filters.Children.Add(refresh);
        import.Click+=(_,_)=>OpenImport();filters.Children.Add(import);
        top.Children.Add(filters);top.Children.Add(status);
        var bottom=new StackPanel{Margin=new Thickness(0,8,0,0)};DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        bottom.Children.Add(new TextBlock{Text="Selected historical record (read-only)",FontWeight=FontWeights.SemiBold});bottom.Children.Add(detail);
        AddColumn("Legacy project ID","Source.LegacyId",125);
        AddColumn("Legacy number","Source.LegacyProjectNumber",135);
        AddColumn("Project title","Source.Title",250);
        AddColumn("Legacy client","Source.LegacyClientId",110);
        AddColumn("Source status","Source.LegacyStatus",125);
        AddColumn("Source authorization","Source.LegacyAuthorizationStatus",145);
        grid.SelectionChanged+=(_,_)=>ShowDetail();root.Children.Add(grid);Content=root;
        Loaded+=(_,_)=>{Refresh();guard.Start();};
        Activated+=(_,_)=>CheckAccess();
        guard.Tick+=(_,_)=>CheckAccess();
        Closed+=(_,_)=>{guard.Stop();Clear();};
    }
    private void AddColumn(string label,string binding,double width)
        =>grid.Columns.Add(new DataGridTextColumn{Header=label,Binding=new Binding(binding),Width=width});
    private void Clear()
    {
        grid.ItemsSource=null;client.ItemsSource=null;detail.Clear();import.IsEnabled=false;
    }
    private void CheckAccess()
    {
        try
        {
            if(!service.Can("HistoricalProjects.View")||!service.Can("Clients.View"))
            {Clear();status.Text="Access denied or session expired.";return;}
            var next=syntheticAcceptance&&service.CanImportHistoricalProjects();
            if(next!=mayImport){mayImport=next;Refresh();}
        }
        catch(Exception){Clear();status.Text="Archive access could not be verified.";}
    }
    private void Refresh()
    {
        var filter=(client.SelectedItem as ClientFilter)?.LegacyId;
        Clear();
        try
        {
            var rows=service.ReadHistoricalProjects();
            mayImport=syntheticAcceptance&&service.CanImportHistoricalProjects();
            var choices=new List<ClientFilter>{new("All clients",null)};
            choices.AddRange(rows.Select(x=>x.Source.LegacyClientId).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).Select(x=>new ClientFilter(x,x)));
            client.ItemsSource=choices;client.SelectedItem=choices.FirstOrDefault(x=>string.Equals(x.LegacyId,filter,StringComparison.OrdinalIgnoreCase))??choices[0];
            filter=(client.SelectedItem as ClientFilter)?.LegacyId;
            var term=search.Text.Trim();
            var filtered=rows.Where(x=>filter==null||string.Equals(x.Source.LegacyClientId,filter,StringComparison.OrdinalIgnoreCase))
                .Where(x=>term.Length==0||new[]{x.Source.LegacyId,x.Source.LegacyProjectNumber,x.Source.Title,x.Source.Site}
                    .Any(v=>v.Contains(term,StringComparison.OrdinalIgnoreCase))).ToArray();
            grid.ItemsSource=filtered;
            status.Text=rows.Count==0?"No sealed historical migration batch.":$"Sealed archive: {filtered.Length} of {rows.Count} historical records.";
            import.IsEnabled=mayImport;
            if(!syntheticAcceptance)status.Text+=" Import review is restricted to the synthetic acceptance workspace.";
        }
        catch(UnauthorizedAccessException){Clear();status.Text="Access denied or session expired.";}
        catch(Exception){Clear();status.Text="Archive unavailable or reconciliation failed; no records displayed.";}
    }
    private void ShowDetail()
    {
        detail.Clear();
        if(grid.SelectedItem is not HistoricalArchiveItem item)return;
        var s=item.Source;
        detail.Text=$"Original project ID: {s.LegacyId}\nOriginal number: {s.LegacyProjectNumber}\nLegacy client: {s.LegacyClientId} · Native client ID: {item.NativeClientId}\nTitle: {s.Title}\nSite: {s.Site}\nSource status: {s.LegacyStatus}\nSource authorization label: {s.LegacyAuthorizationStatus}\nWork authorized: No";
        if(mayImport&&!string.IsNullOrEmpty(s.AuthorizationEvidenceRef))
            detail.Text+="\nUnverified historical evidence reference: "+s.AuthorizationEvidenceRef;
    }
    private void OpenImport()
    {
        try
        {
            if(!syntheticAcceptance||!service.CanImportHistoricalProjects())throw new UnauthorizedAccessException();
            new HistoricalProjectImportWindow(service){Owner=this}.ShowDialog();
            Refresh();
        }
        catch(UnauthorizedAccessException){Clear();status.Text="Archive import access denied.";}
        catch(Exception){Clear();status.Text="Archive import window unavailable.";}
    }
}
