using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
namespace PracticeManager.Core;

// Read-only candidate. No signing, trust-store modification, process launch
// or production authorization. Both pins must originate outside the package.
// Windows trust provider result and the leaf from that same verified state
// are checked, not merely the presence of an embedded certificate.
internal static class InstallerExecutableAuthenticity
{
 static bool Hex(string? text)=>text is {Length:64}&&text.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
 static void Require(bool ok)
 {if(!ok)throw new ArgumentException("Installer executable authenticity requires review.");}
 static string Hash(Stream file)
 {file.Position=0;return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();}
 internal static void Review(string executable,string expectedSha256,string independentlyTrustedCertificateSha256)
 {
  Require(Hex(expectedSha256)&&Hex(independentlyTrustedCertificateSha256));
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows trust verification required.");
  var path=LocalStoragePath.FilePath(executable,mustExist:true,rejectSidecars:true);
  Require(WorkspaceConfidentiality.HasWorkspaceFileAcl(path));
  using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
  var length=file.Length;
  Require(length is >0 and <=1024L*1024*1024&&Hash(file)==expectedSha256);
  Require(VerifyWindows(file,path)==independentlyTrustedCertificateSha256);
  Require(file.Length==length&&Hash(file)==expectedSha256);
 }
 [StructLayout(LayoutKind.Sequential)]
 struct FileInfoNative
 {
  public uint Size;
  public IntPtr Path,Handle,KnownSubject;
 }
 [StructLayout(LayoutKind.Sequential)]
 struct TrustData
 {
  public uint Size;
  public IntPtr Policy,Sip;
  public uint UI,Revocation,Choice;
  public IntPtr File;
  public uint StateAction;
  public IntPtr State,Url;
  public uint Flags,Context;
  public IntPtr SignatureSettings;
 }
 [StructLayout(LayoutKind.Sequential)]
 struct ProviderCertPrefix
 {
  public uint Size;
  public IntPtr Certificate;
 }
 [StructLayout(LayoutKind.Sequential)]
 struct CertContextPrefix
 {
  public uint Encoding;
  public IntPtr Encoded;
  public uint Length;
 }
 [DllImport("wintrust.dll",ExactSpelling=true)]
 [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
 static extern int WinVerifyTrust(IntPtr window,ref Guid action,ref TrustData data);
 [DllImport("wintrust.dll",ExactSpelling=true)]
 [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
 static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
 [DllImport("wintrust.dll",ExactSpelling=true)]
 [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
 static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider,uint index,int counterSigner,uint counterIndex);
 [DllImport("wintrust.dll",ExactSpelling=true)]
 [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
 static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer,uint index);
 [SupportedOSPlatform("windows")]
 static string VerifyWindows(FileStream file,string path)
 {
  var action=new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
  var nativePath=Marshal.StringToCoTaskMemUni(path);
  IntPtr nativeFile=IntPtr.Zero;
  try
  {
   nativeFile=Marshal.AllocHGlobal(Marshal.SizeOf<FileInfoNative>());
   var info=new FileInfoNative{Size=(uint)Marshal.SizeOf<FileInfoNative>(),Path=nativePath,Handle=file.SafeFileHandle.DangerousGetHandle()};
   Marshal.StructureToPtr(info,nativeFile,false);
   var data=new TrustData{Size=(uint)Marshal.SizeOf<TrustData>(),UI=2,Revocation=1,Choice=1,File=nativeFile,StateAction=1,
    // Whole-chain revocation, cached only; no fallback to disabled revocation.
    // Lifetime signing and MD2/MD4 rejection. No UI and no network retrieval.
    Flags=0x40|0x800|0x1000|0x2000};
   try
   {
    Require(WinVerifyTrust(new IntPtr(-1),ref action,ref data)==0&&data.State!=IntPtr.Zero);
    var provider=WTHelperProvDataFromStateData(data.State);Require(provider!=IntPtr.Zero);
    var signer=WTHelperGetProvSignerFromChain(provider,0,0,0);Require(signer!=IntPtr.Zero);
    var leaf=WTHelperGetProvCertFromChain(signer,0);Require(leaf!=IntPtr.Zero);
    var cert=Marshal.PtrToStructure<ProviderCertPrefix>(leaf);
    Require(cert.Size>=Marshal.SizeOf<ProviderCertPrefix>()&&cert.Certificate!=IntPtr.Zero);
    var context=Marshal.PtrToStructure<CertContextPrefix>(cert.Certificate);
    Require(context.Encoded!=IntPtr.Zero&&context.Length is >0 and <=65536);
    var bytes=new byte[(int)context.Length];Marshal.Copy(context.Encoded,bytes,0,bytes.Length);
    return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
   }
   finally
   {
    data.StateAction=2;
    Require(WinVerifyTrust(new IntPtr(-1),ref action,ref data)==0);
    GC.KeepAlive(file);
   }
  }
  finally
  {
   if(nativeFile!=IntPtr.Zero)Marshal.FreeHGlobal(nativeFile);
   Marshal.FreeCoTaskMem(nativePath);
  }
 }
}
