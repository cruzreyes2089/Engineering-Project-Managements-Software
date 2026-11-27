using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using PracticeManager.Core;
using PracticeManager.Persistence;

namespace PracticeManager.Desktop;

internal sealed class HistoricalProjectImportWindow:Window
{
    private readonly LocalPractice service;
    private readonly TextBox[] hashes=Enumerable.Range(0,4).Select(_=>new TextBox{MaxLength=64}).ToArray();
    private readonly string?[] paths=new string?[3];
    private readonly TextBlock[] selected=Enumerable.Range(0,3).Select(_=>new TextBlock{Text="Not selected"}).ToArray();
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly TextBox note=new(){MaxLength=1000,AcceptsReturn=true,Height=72,TextWrapping=TextWrapping.Wrap};
    private readonly CheckBox confirmed=new(){Content="I reviewed the synthetic source, independent census, field mapping and recovery copy."};
    private readonly Button commit=new(){Content="Commit exact reviewed archive",IsEnabled=false};
    private readonly DispatcherTimer guard=new(){Interval=TimeSpan.FromSeconds(20)};
    private byte[][]? snapshots;
    private string[]? reviewedHashes;
    private HistoricalArchivePreview? preview;
    public HistoricalProjectImportWindow(LocalPractice service)
    {
        this.service=service;Title="Historical project import — synthetic acceptance only";Width=790;Height=790;
        MinWidth=640;MinHeight=520;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var p=new StackPanel{Margin=new Thickness(20)};
        p.Children.Add(new TextBlock{Text="Review historical archive import",FontSize=24,FontWeight=FontWeights.SemiBold});
        p.Children.Add(new TextBlock{Text="Use only disposable synthetic acceptance data. Obtain all four SHA-256 values from independent review records. No legacy status, evidence reference or import action authorizes work.",TextWrapping=TextWrapping.Wrap});
        var names=new[]{"Original Access source SHA-256","Reviewed CRM transfer SHA-256","Reviewed project transfer SHA-256","Independent project census SHA-256"};
        for(int i=0;i<hashes.Length;i++)
        {
            p.Children.Add(new TextBlock{Text=names[i]});p.Children.Add(hashes[i]);
            hashes[i].TextChanged+=(_,_)=>InvalidateReview();
        }
        var files=new[]{"CRM transfer","Historical project transfer","Independent census"};
        for(int i=0;i<files.Length;i++)
        {
            var index=i;var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,3,0,4)};
            var choose=new Button{Content="Select "+files[i]};choose.Click+=(_,_)=>Run(()=>Choose(index));
            row.Children.Add(choose);row.Children.Add(selected[i]);p.Children.Add(row);
        }
        var validate=new Button{Content="Validate and preview exact package"};validate.Click+=(_,_)=>Run(Preview);
        p.Children.Add(validate);p.Children.Add(status);
        p.Children.Add(new TextBlock{Text="Review note"});p.Children.Add(note);p.Children.Add(confirmed);
        confirmed.Checked+=(_,_)=>UpdateCommit();confirmed.Unchecked+=(_,_)=>UpdateCommit();
        note.TextChanged+=(_,_)=>UpdateCommit();
        commit.Click+=(_,_)=>Run(Commit);p.Children.Add(commit);
        Content=new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        Loaded+=(_,_)=>{guard.Start();CheckAccess();};guard.Tick+=(_,_)=>CheckAccess();
        Closed+=(_,_)=>{guard.Stop();InvalidateReview();Array.Clear(paths);note.Clear();};
    }
    private void CheckAccess()
    {
        try{if(!service.CanImportHistoricalProjects()){InvalidateReview();Close();}}
        catch(Exception){InvalidateReview();Close();}
    }
    private void UpdateCommit()=>commit.IsEnabled=preview?.TargetStatus=="ReadyForExplicitReview"&&
        confirmed.IsChecked==true&&!string.IsNullOrWhiteSpace(note.Text);
    private void InvalidateReview()
    {
        if(snapshots!=null)foreach(var bytes in snapshots)if(bytes!=null)Array.Clear(bytes);
        snapshots=null;reviewedHashes=null;preview=null;confirmed.IsChecked=false;note.Clear();commit.IsEnabled=false;
        status.Text="Package requires a fresh preview.";
    }
    private void Run(Action action)
    {
        try{if(!service.CanImportHistoricalProjects())throw new UnauthorizedAccessException();action();}
        catch(UnauthorizedAccessException){InvalidateReview();Close();}
        catch(Exception){InvalidateReview();status.Text="Review rejected or operation failed. No successful import assumed; refresh the register before retrying.";}
    }
    private void Choose(int index)
    {
        InvalidateReview();paths[index]=null;selected[index].Text="Not selected";
        var picker=new OpenFileDialog{Title="Select reviewed synthetic JSON",Filter="JSON (*.json)|*.json",CheckFileExists=true};
        if(picker.ShowDialog(this)!=true)return;
        paths[index]=Path.GetFullPath(picker.FileName);selected[index].Text="File selected";
    }
    private static byte[] Read(string path)
    {
        using var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(f.Length is <1 or >HistoricalProjectTransfer.MaxBytes)throw new ArgumentException("Invalid package size.");
        var bytes=new byte[(int)f.Length];
        f.ReadExactly(bytes);
        if(f.ReadByte()!=-1){Array.Clear(bytes);throw new ArgumentException("Package changed during read.");}
        return bytes;
    }
    private void Preview()
    {
        InvalidateReview();
        if(paths.Any(x=>x==null)||paths.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=3)
            throw new ArgumentException("Select three distinct files.");
        reviewedHashes=hashes.Select(x=>x.Text.Trim()).ToArray();
        snapshots=new byte[3][];
        for(int i=0;i<3;i++)snapshots[i]=Read(paths[i]!);
        var h=reviewedHashes;var b=snapshots;
        preview=service.PreviewHistoricalProjects(b[0],b[1],b[2],h[0],h[1],h[2],h[3]);
        status.Text=$"Target: {preview.TargetStatus}\nHistorical projects: {preview.Count}; duplicate number groups: {preview.DuplicateNumberGroups}\nProject SHA-256: {preview.ProjectSha256}\nCensus SHA-256: {preview.CensusSha256}\nValidation only. No work authorization.";
        UpdateCommit();
    }
    private void Commit()
    {
        if(preview?.TargetStatus!="ReadyForExplicitReview"||snapshots==null||reviewedHashes==null||
            confirmed.IsChecked!=true||string.IsNullOrWhiteSpace(note.Text))throw new ArgumentException("Preview and review required.");
        if(MessageBox.Show(this,"Commit this exact synthetic archive? No operational project or authorization will be created.",
            "Confirm synthetic archive",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        var b=snapshots;var h=reviewedHashes;
        var outcome=service.ImportHistoricalProjects(b[0],b[1],b[2],h[0],h[1],h[2],h[3],true,note.Text);
        InvalidateReview();note.Clear();status.Text=outcome+". Refresh the archive register to verify sealed records.";
    }
}
