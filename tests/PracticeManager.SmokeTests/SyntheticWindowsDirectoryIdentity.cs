using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// Windows-only, synthetic qualification. Not a production path or ACL security boundary.
internal static class SyntheticWindowsDirectoryIdentity
{
 const uint ReadAttributes=0x80, ShareReadWrite=3, OpenExisting=3;
 const uint BackupSemantics=0x02000000, OpenReparsePoint=0x00200000;
 const uint DirectoryAttribute=0x10, ReparseAttribute=0x400;
 [StructLayout(LayoutKind.Sequential)]
 struct Information
 {
  public uint Attributes;
  public uint CreationLow,CreationHigh,AccessLow,AccessHigh,WriteLow,WriteHigh;
  public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;
 }
 [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true,ExactSpelling=true)]
 static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
 [DllImport("kernel32.dll",SetLastError=true)]
 [return:MarshalAs(UnmanagedType.Bool)]
 static extern bool GetFileInformationByHandle(SafeFileHandle handle,out Information information);
 internal sealed class Pin:IDisposable
 {
  readonly SafeFileHandle handle;
  public string Identity{get;}
  internal Pin(SafeFileHandle handle,string identity){this.handle=handle;Identity=identity;}
  public void Dispose()=>handle.Dispose();
 }
 public static Pin Open(string path)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows directory identity required.");
  // Do not follow a reparse point in the final path component. Omit delete sharing
  // while this handle is held. Ancestor checks are still separate.
  var handle=CreateFile(path,ReadAttributes,ShareReadWrite,IntPtr.Zero,OpenExisting,BackupSemantics|OpenReparsePoint,IntPtr.Zero);
  try
  {
   if(handle.IsInvalid||!GetFileInformationByHandle(handle,out var info))throw new IOException("Synthetic directory identity unavailable.");
   if((info.Attributes&DirectoryAttribute)==0||(info.Attributes&ReparseAttribute)!=0)
    throw new IOException("Synthetic directory identity is not a plain directory.");
   if(info.IndexHigh==0&&info.IndexLow==0)throw new IOException("Synthetic directory file index unavailable.");
   return new Pin(handle,$"{info.Volume:x8}:{info.IndexHigh:x8}{info.IndexLow:x8}");
  }
  catch{handle.Dispose();throw;}
 }
 public static string Observe(string path)
 {
  using var pin=Open(path);return pin.Identity;
 }
}
