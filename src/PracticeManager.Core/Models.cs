using System.Security.Cryptography;
using System.Text;
namespace PracticeManager.Core;
public sealed record Client(long Id,string Number,string Type,string Company,string First,string Last,string Email,string Phone,string Address1,string Address2,string City,string State,string Zip,string Notes,string Status){ public string DisplayName => string.IsNullOrWhiteSpace(Company) ? (First+" "+Last).Trim() : Company; }
public sealed record Contact(long Id,long ClientId,string First,string Last,string Title,string Email,string Phone,bool Primary){public string Name=>(First+" "+Last).Trim();}
public static class PasswordCodec
{
 const int Iterations=600_000;
 const string Prefix="pbkdf2-sha256$600000$";
 public static string Hash(string password)
 {
  if(password is null||password.Length is <14 or >256)throw new ArgumentException("Use a password of 14–256 characters.");
  var salt=RandomNumberGenerator.GetBytes(16);
  var derived=Rfc2898DeriveBytes.Pbkdf2(password,salt,Iterations,HashAlgorithmName.SHA256,32);
  try{return Prefix+Convert.ToBase64String(salt)+"$"+Convert.ToBase64String(derived);}
  finally{CryptographicOperations.ZeroMemory(derived);}
 }
 public static bool Verify(string password,string encoded)
 {
  // Bound parsing and derivation work before any allocation based on persisted input.
  if(password is null||password.Length>256||encoded is null||
     encoded.Length!=Prefix.Length+24+1+44||!encoded.StartsWith(Prefix,StringComparison.Ordinal)||
     encoded[Prefix.Length+24]!='$')return false;
  Span<byte> salt=stackalloc byte[16];Span<byte> expected=stackalloc byte[32];
  try
  {
   var saltText=encoded.AsSpan(Prefix.Length,24);
   var hashText=encoded.AsSpan(Prefix.Length+25,44);
   if(!Convert.TryFromBase64Chars(saltText,salt,out var saltBytes)||saltBytes!=16||
      !Convert.TryFromBase64Chars(hashText,expected,out var hashBytes)||hashBytes!=32||
      !saltText.SequenceEqual(Convert.ToBase64String(salt).AsSpan())||
      !hashText.SequenceEqual(Convert.ToBase64String(expected).AsSpan()))return false;
   var derived=Rfc2898DeriveBytes.Pbkdf2(password,salt,Iterations,HashAlgorithmName.SHA256,32);
   try{return CryptographicOperations.FixedTimeEquals(expected,derived);}
   finally{CryptographicOperations.ZeroMemory(derived);}
  }
  finally{CryptographicOperations.ZeroMemory(expected);CryptographicOperations.ZeroMemory(salt);}
 }
}
public static class Validation
{
 public static IReadOnlyList<string> ClientTypes {get;}=Array.AsReadOnly(new[]{"Individual / Homeowner","Business / Company","Contractor / Developer","Architect / Designer","Property Manager / HOA","Government / Public Agency","Other"});
 public static string Text(string? value,int max,string label,bool required=false){var v=(value??"").Trim();if(v.Length>max||(required&&v.Length==0))throw new ArgumentException($"{label}: {(required?"required, ":"")}maximum {max} characters.");return v;}
 // US/NANP development default; no extensions or international-number guessing.
 public static string Phone(string? value)
 {
  var input=Text(value,40,"Phone");
  if(input.Length==0)return "";
  if(input.Any(c=>!char.IsAsciiDigit(c)&&c is not (' ' or '-' or '.' or '(' or ')' or '+')))
   throw new ArgumentException("Phone: enter 10 digits, optionally preceded by country code 1.");
  var digits=new string(input.Where(char.IsAsciiDigit).ToArray());
  var plus=input.IndexOf('+');
  if(plus>=0&&(plus!=0||input.LastIndexOf('+')!=0||digits.Length!=11||digits[0]!='1'))
   throw new ArgumentException("Phone: only country code +1 is supported.");
  if(digits.Length==11&&digits[0]=='1')digits=digits[1..];
  if(digits.Length!=10)throw new ArgumentException("Phone: enter 10 digits, optionally preceded by country code 1.");
  return $"({digits[..3]}){digits[3..6]}-{digits[6..]}";
 }
 // Preserve nonconforming legacy text on read; never silently discard information.
 public static string PhoneForDisplay(string? value)
 {
  try{return Phone(value);}catch(ArgumentException){return value??"";}
 }
 public static string Username(string value){var s=Text(value,60,"Username",true); if(s.Any(c=>!(char.IsAsciiLetterOrDigit(c)||c is '.' or '_' or '-')))throw new ArgumentException("Username must use letters, numbers, dots, underscores or hyphens.");return s.ToUpperInvariant();}
}
