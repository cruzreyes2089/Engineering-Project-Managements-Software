using System.Text.Json;
namespace PracticeManager.Core;
public sealed record AccountingAccount(Guid Key,string Name,string? Code)
{
 public override string ToString()=>"[Accounting account]";
}
public sealed record AccountingAccountRegister(string BusinessName,IReadOnlyList<AccountingAccount> Rows)
{
 public override string ToString()=>$"Account register: {Rows.Count} rows";
}
internal static class AccountingAccountParser
{
 static bool Unique(JsonElement v)=>v.ValueKind==JsonValueKind.Object&&v.EnumerateObject().Select(x=>x.Name).Distinct(StringComparer.Ordinal).Count()==v.EnumerateObject().Count();
 static bool ReadText(JsonElement v,string prop,int max,out string result)
 {
  result="";if(!v.TryGetProperty(prop,out var p)||p.ValueKind!=JsonValueKind.String)return false;
  result=p.GetString()??"";return result.Length<=max&&!result.Any(char.IsControl);
 }
 internal static AccountingAccountRegister? Parse(JsonElement root)
 {
  if(!Unique(root)||!root.TryGetProperty("business",out var b)||!Unique(b)||!ReadText(b,"name",512,out var business)||string.IsNullOrWhiteSpace(business)||!root.TryGetProperty("totalRecords",out var count)||count.ValueKind!=JsonValueKind.Number||!count.TryGetInt32(out var n)||n<0||n>10000||!root.TryGetProperty("chartOfAccounts",out var a)||a.ValueKind!=JsonValueKind.Array||a.GetArrayLength()!=n)return null;
  var rows=new List<AccountingAccount>(n);var ids=new HashSet<Guid>();
  foreach(var r in a.EnumerateArray()){
   if(!Unique(r)||!ReadText(r,"key",64,out var k)||!Guid.TryParse(k,out var id)||id==Guid.Empty||!ids.Add(id)||!ReadText(r,"name",512,out var name)||string.IsNullOrWhiteSpace(name)||!r.TryGetProperty("code",out var c))return null;
   string? code=null;if(c.ValueKind!=JsonValueKind.Null){if(!ReadText(r,"code",128,out var value))return null;code=value;}
   rows.Add(new(id,name,code));
  }
  return new(business,rows.AsReadOnly());
 }
}
