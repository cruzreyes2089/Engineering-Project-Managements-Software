using System.IO;
using System.Windows;
using PracticeManager.Core;

namespace PracticeManager.Desktop;
public partial class App : Application
{
 IDisposable? installedLease;
 protected override void OnStartup(StartupEventArgs e)
 {
  try
  {
   var profile=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
   var root=Path.GetFullPath(Path.Combine(profile,"Programs","CRC Practice Manager"));
   var executable=Path.GetFullPath(Environment.ProcessPath??throw new InvalidOperationException());
   var installed=executable.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
   if(installed)
   {
    // Only isolated acceptance is permitted in installed code at this stage.
    // Do not silently open DEV or enable production from an installed shortcut.
    if(!(e.Args.Length==1&&e.Args[0]=="--production-acceptance-new")&&
       !(e.Args.Length==2&&e.Args[0]=="--production-acceptance-session"))
     throw new ArgumentException("Installed production startup remains disabled.");
    installedLease=InstalledVersionSelectionCandidate.AcquireSelectedLaunchLease(
      InstalledProgramBoundary.ReviewExisting(profile),executable);
   }
   else if(e.Args.Contains("--installed",StringComparer.Ordinal))
    throw new ArgumentException("Installed program location required.");
  }
  catch(Exception)
  {
   installedLease?.Dispose();installedLease=null;
   MessageBox.Show("Cannot start the selected installed program. Production startup is not enabled or installation review is required.","CRC Practice Manager",MessageBoxButton.OK,MessageBoxImage.Warning);
   Shutdown(1);return;
  }
  base.OnStartup(e);
 }
 protected override void OnExit(ExitEventArgs e)
 {
  try{base.OnExit(e);}
  finally{installedLease?.Dispose();installedLease=null;}
 }
}
