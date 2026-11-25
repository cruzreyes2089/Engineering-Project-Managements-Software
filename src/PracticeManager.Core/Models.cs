using System.Security.Cryptography;
using System.Text;
namespace PracticeManager.Core;
public sealed record Client(long Id,string Number,string Type,string Company,string First,string Last,string Email,string Phone,string Address1,string Address2,string City,string State,string Zip,string Notes,string Status){ public string DisplayName => string.IsNullOrWhiteSpace(Company) ? (First+" "+Last).Trim() : Company; }
public sealed record Contact(long Id,long ClientId,string First,string Last,string Title,string Email,string Phone,bool Primary){public string Name=>(First+" "+Last).Trim();}
public static class PasswordCodec
{
 const int Iterations=600_000;
 public static string Hash(string password){ if(password.Length<14 || password.Length>256)throw new ArgumentException("Use a password of 14–256 characters."); var salt=RandomNumberGenerator.GetBytes(16);var hash=Rfc2898DeriveBytes.Pbkdf2(password,salt,Iterations,HashAlgorithmName.SHA256,32);return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}"; }
 public static bool Verify(string password,string encoded){ if(password.Length>256)return false;try{var p=encoded.Split('$');if(p.Length!=4||p[0]!="pbkdf2-sha256"||p[1]!=Iterations.ToString())return false;var salt=Convert.FromBase64String(p[2]);var hash=Convert.FromBase64String(p[3]);if(salt.Length!=16||hash.Length!=32)return false;return CryptographicOperations.FixedTimeEquals(hash,Rfc2898DeriveBytes.Pbkdf2(password,salt,Iterations,HashAlgorithmName.SHA256,32));}catch(FormatException){return false;}}
}
public static class Validation
{
 public static IReadOnlyList<string> ClientTypes {get;}=Array.AsReadOnly(new[]{"Individual / Homeowner","Business / Company","Contractor / Developer","Architect / Designer","Property Manager / HOA","Government / Public Agency","Other"});
 public static string Text(string? value,int max,string label,bool required=false){var v=(value??"").Trim();if(v.Length>max||(required&&v.Length==0))throw new ArgumentException($"{label}: {(required?"required, ":"")}maximum {max} characters.");return v;}
 public static string Username(string value){var s=Text(value,60,"Username",true); if(s.Any(c=>!(char.IsAsciiLetterOrDigit(c)||c is '.' or '_' or '-')))throw new ArgumentException("Username must use letters, numbers, dots, underscores or hyphens.");return s.ToUpperInvariant();}
}
