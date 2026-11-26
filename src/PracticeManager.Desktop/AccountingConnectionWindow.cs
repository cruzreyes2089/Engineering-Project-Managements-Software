using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PracticeManager.Persistence;

namespace PracticeManager.Desktop;

internal sealed class AccountingConnectionWindow : Window
{
    private readonly LocalPractice service;
    private readonly TextBox endpoint=new(){MaxLength=150,Margin=new Thickness(0,4,0,12)};
    private readonly PasswordBox secret=new(){MaxLength=4096,Margin=new Thickness(0,4,0,8)};
    private readonly Button paste=new(){Content="Paste secret",Padding=new Thickness(12,6,12,6)};
    private readonly Button test=new(){Content="Test connection",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,12,0,0)};
    private readonly TextBlock result=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};
    private readonly Button save=new(){Content="Test and save connection",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,8,0,0)};
    private readonly Button savedTest=new(){Content="Test saved connection",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,8,0,0)};
    private readonly Button inspect=new(){Content="Inspect customer schema (read-only)",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,8,0,0)};
    private readonly TextBox schema=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,MaxHeight=240,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Visibility=Visibility.Collapsed,Margin=new Thickness(0,8,0,0)};
    private readonly Button customers=new(){Content="Open customer register (read-only)",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,8,0,0)};
    private readonly Button accounts=new(){Content="Open chart of accounts (read-only)",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,8,0,0)};
    private readonly Button discovery=new(){Content="Inspect integration schemas (read-only)",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,8,0,0)};
    private readonly Button marker=new(){Content="Verify accounting business marker",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,8,0,0)};
    private readonly Button financial=new(){Content="Open financial preview (read-only)",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,8,0,0)};
    private readonly Button directory=new(){Content="Customer sync and account roles (development)",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,8,0,0)};
    private readonly Button remove=new(){Content="Remove saved connection",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,8,0,0)};
    private readonly TextBlock profileStatus=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,12)};
    private CancellationTokenSource? pending;
    private bool closed;
    public AccountingConnectionWindow(LocalPractice service)
    {
        this.service=service;
        Title="Accounting connection — development only";Width=580;SizeToContent=SizeToContent.Height;
        ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        Background=Brushes.White;
        var layout=new StackPanel{Margin=new Thickness(24)};
        layout.Children.Add(new TextBlock{Text="Manager.io Desktop",FontSize=23,FontWeight=FontWeights.SemiBold});
        layout.Children.Add(new TextBlock{Text="Read-only customer endpoint test. Open the synthetic Manager business first. No posting or synchronization.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,18)});
        layout.Children.Add(new TextBlock{Text="API2 endpoint (copy from Manager access token screen)"});
        layout.Children.Add(endpoint);
        layout.Children.Add(new TextBlock{Text="Example: http://127.0.0.1:PORT/api2",Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,12)});
        layout.Children.Add(profileStatus);
        layout.Children.Add(new TextBlock{Text="Secret key (saved only by explicit Test and save)"});
        layout.Children.Add(secret);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};buttons.Children.Add(paste);
        var clear=new Button{Content="Clear secret",Padding=new Thickness(12,6,12,6),Margin=new Thickness(8,0,0,0)};
        clear.Click+=(_,_)=>{secret.Clear();result.Text="Secret field cleared.";};buttons.Children.Add(clear);
        layout.Children.Add(buttons);
        layout.Children.Add(new TextBlock{Text="Paste reads the Windows clipboard and clears its current value when possible. Windows clipboard history is separate.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,8,0,0)});
        layout.Children.Add(test);layout.Children.Add(save);layout.Children.Add(savedTest);layout.Children.Add(inspect);layout.Children.Add(customers);layout.Children.Add(accounts);layout.Children.Add(discovery);layout.Children.Add(marker);layout.Children.Add(financial);layout.Children.Add(directory);layout.Children.Add(remove);layout.Children.Add(result);layout.Children.Add(schema);
        var close=new Button{Content="Close",HorizontalAlignment=HorizontalAlignment.Right,Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,12,0,0)};
        close.Click+=(_,_)=>Close();layout.Children.Add(close);
        Content=new ScrollViewer{Content=layout,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};MaxHeight=SystemParameters.WorkArea.Height-40;
        paste.Click+=Paste_Click;test.Click+=async (_,_)=>await ExecuteAsync("test");save.Click+=async (_,_)=>await ExecuteAsync("save");savedTest.Click+=async (_,_)=>await ExecuteAsync("saved");inspect.Click+=async (_,_)=>await ExecuteAsync("inspect");
        discovery.Click+=(_,_)=>{if(pending==null)new AccountingDiscoveryWindow(service){Owner=this}.ShowDialog();};remove.Click+=(_,_)=>RemoveProfile();customers.Click+=(_,_)=>{if(pending==null)new AccountingCustomerWindow(service){Owner=this}.ShowDialog();};
        accounts.Click+=(_,_)=>{if(pending==null)new AccountingAccountsWindow(service){Owner=this}.ShowDialog();};
        marker.Click+=(_,_)=>{if(pending==null&&service.CanManageBusinessMarker())new AccountingMarkerWindow(service){Owner=this}.ShowDialog();};
        financial.Click+=(_,_)=>{if(pending==null&&service.CanManageBusinessMarker())new AccountingFinancialWindow(service){Owner=this}.ShowDialog();};
        directory.Click+=(_,_)=>{if(pending==null&&service.CanSyncDirectory())new AccountingDirectoryWindow(service){Owner=this}.ShowDialog();};
        RefreshProfile(true);
        endpoint.TextChanged+=(_,_)=>{if(pending==null)result.Text="";};
        Closed+=(_,_)=>{closed=true;secret.Clear();pending?.Cancel();};
    }
    private void Paste_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            if(!service.CanTestAccountingConnection())throw new UnauthorizedAccessException();
            var value=Clipboard.GetText().Trim();
            if(value.Length==0||value.Length>4096||value.Any(c=>c<33||c>126))
            {result.Text="Clipboard does not contain a valid secret. Copy only the key from Manager.";return;}
            secret.Password=value;
            try{Clipboard.Clear();result.Text="Secret pasted. Clipboard history may still contain a copy.";}
            catch(System.Runtime.InteropServices.ExternalException){result.Text="Secret pasted. Clear your clipboard manually.";}
        }
        catch(UnauthorizedAccessException){secret.Clear();result.Text="Access denied. Sign in again.";}
        catch(System.Runtime.InteropServices.ExternalException){result.Text="Clipboard unavailable. Copy again or enter the key manually.";}
    }
    private void RefreshProfile(bool fillEndpoint)
    {
        var p=service.ReadAccountingProfile();
        profileStatus.Text=p==null?"No saved connection.":$"Saved endpoint: {p.Endpoint}\nLast successful test (UTC): {p.LastSuccessUtc}";
        if(fillEndpoint&&p!=null)endpoint.Text=p.Endpoint;
        save.IsEnabled=service.CanManageAccountingConnection();
        remove.IsEnabled=p!=null&&service.CanManageAccountingConnection();
        savedTest.IsEnabled=inspect.IsEnabled=customers.IsEnabled=accounts.IsEnabled=discovery.IsEnabled=p!=null;
        marker.IsEnabled=financial.IsEnabled=p!=null&&service.CanManageBusinessMarker();
        directory.IsEnabled=p!=null&&service.CanSyncDirectory();
    }
    private void RemoveProfile()
    {
        if(pending!=null)return;
        schema.Clear();schema.Visibility=Visibility.Collapsed;
        if(MessageBox.Show(this,"Remove the saved connection from this CRC profile? This does not revoke the Manager token.","Remove connection",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        try{service.RemoveAccountingProfile();secret.Clear();endpoint.Clear();RefreshProfile(false);result.Text="Saved connection removed. No Manager records changed.";}
        catch(UnauthorizedAccessException){result.Text="Access denied.";}
        catch(Exception){result.Text="Removal failed. No successful removal is assumed.";}
    }
    private async Task ExecuteAsync(string mode)
    {
        if(pending!=null)return;
        using var request=new CancellationTokenSource();pending=request;
        var key=secret.Password.Trim();secret.Clear();
        test.IsEnabled=paste.IsEnabled=endpoint.IsEnabled=secret.IsEnabled=save.IsEnabled=savedTest.IsEnabled=inspect.IsEnabled=customers.IsEnabled=accounts.IsEnabled=discovery.IsEnabled=marker.IsEnabled=financial.IsEnabled=directory.IsEnabled=remove.IsEnabled=false;
        schema.Clear();schema.Visibility=Visibility.Collapsed;
        result.Foreground=Brushes.DimGray;result.Text="Testing local connection...";
        try
        {
            var r=mode switch
            {
                "save"=>await service.SaveAccountingProfileAsync(endpoint.Text,key,request.Token),
                "saved"=>await service.TestSavedAccountingProfileAsync(request.Token),
                "inspect"=>await service.InspectSavedCustomersAsync(request.Token),
                _=>await service.TestAccountingConnectionAsync(endpoint.Text,key,request.Token)
            };
            if(closed)return;
            if(mode=="inspect"&&r.Success&&r.SchemaSummary!=null){schema.Text=r.SchemaSummary;schema.Visibility=Visibility.Visible;}
            result.Foreground=r.Success?Brushes.DarkGreen:Brushes.DarkRed;
            result.Text=r.Outcome switch
            {
                "Connected"=>$"API CONNECTION SUCCESSFUL — HTTP {r.StatusCode}. Customer response: JSON {r.JsonShape}. No records imported."+(mode=="save"?" Connection saved.":""),
                "CredentialUnavailable"=>"Windows credential protection unavailable. Re-enter and save the connection under your Windows account.",
                "NotConfigured"=>"No saved connection.",
                "ProfileChanged"=>"Connection changed during testing. Reopen the dialog and try again.",
                "MarkerMissing"=>"Enrolled marker missing. Connection not accepted.",
                "MarkerMismatch"=>"Enrolled marker UUID differs. Connection not accepted.",
                "MarkerAmbiguous"=>"Marker is ambiguous. Connection not accepted.",
                "InvalidMarkerResponse"=>"Marker response invalid. Connection not accepted.",
                "MarkerChanged"=>"Marker state changed. Retry.",
                "InvalidEndpoint"=>"Enter the exact local HTTP 127.0.0.1 port and /api2 address from Manager.",
                "MissingSecret"=>"Enter or paste the secret key again. Only a valid nonempty key is accepted.",
                "Unauthorized"=>$"HTTP {r.StatusCode} — key invalid or access denied.",
                "NotFound"=>"HTTP 404 — check the current Manager API2 endpoint.",
                "RedirectBlocked"=>"Redirect blocked. Check the local endpoint.",
                "InvalidResponse"=>"Invalid customer response. Expected a JSON object or array.",
                "ResponseTooLarge"=>"Response exceeds the 1 MB diagnostic limit.",
                "Timeout"=>"Connection timed out. Check that Manager is running.",
                "Cancelled"=>"Connection cancelled.",
                "HttpError"=>$"Manager returned HTTP {r.StatusCode}.",
                _=>"Cannot connect. Keep Manager open and verify its current port."
            };
        }
        catch(UnauthorizedAccessException){if(!closed)result.Text="Access denied. Sign in again as administrator.";}
        catch(Exception){if(!closed)result.Text="Diagnostic failed. No successful connection is assumed.";}
        finally{pending=null;if(!closed){test.IsEnabled=paste.IsEnabled=endpoint.IsEnabled=secret.IsEnabled=true;secret.Clear();try{RefreshProfile(false);}catch{save.IsEnabled=savedTest.IsEnabled=inspect.IsEnabled=customers.IsEnabled=accounts.IsEnabled=discovery.IsEnabled=marker.IsEnabled=financial.IsEnabled=directory.IsEnabled=remove.IsEnabled=false;}}}
    }
}
