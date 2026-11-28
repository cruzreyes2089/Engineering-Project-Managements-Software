using System.Text.Json;
using System.Text.RegularExpressions;
namespace PracticeManager.Core;
internal static class AccountingCustomerPageParser
{
 private static bool UniqueObject(JsonElement value)=>value.ValueKind==JsonValueKind.Object&&value.EnumerateObject().Select(x=>x.Name).Distinct(StringComparer.Ordinal).Count()==value.EnumerateObject().Count();
 private static bool Int(JsonElement root,string field,out int value)
 {
  value=0;return root.TryGetProperty(field,out var p)&&p.ValueKind==JsonValueKind.Number&&p.TryGetInt32(out value);
 }
 private static bool Text(JsonElement root,string field,int max,out string value)
 {
  value="";
  if(!root.TryGetProperty(field,out var p)||p.ValueKind!=JsonValueKind.String)return false;
  value=p.GetString()??"";
  return value.Length<=max&&!value.Any(char.IsControl);
 }
 private static string Label(string name)=>Regex.IsMatch(name,@"\A[A-Za-z_][A-Za-z0-9_]{0,47}\z",RegexOptions.CultureInvariant)&&!Guid.TryParse(name,out _) ?name:"[field name omitted]";
 private static string Type(JsonElement value)=>value.ValueKind==JsonValueKind.String&&Guid.TryParse(value.GetString(),out _)?"String (UUID-shaped)":value.ValueKind is JsonValueKind.True or JsonValueKind.False?"Boolean":value.ValueKind.ToString();
 internal static AccountingCustomerPage? Parse(JsonElement root,int requested)
 {
  if(!UniqueObject(root)||!Int(root,"skip",out var skip)||skip!=requested||!Int(root,"pageSize",out var size)||size<1||size>20||!Int(root,"totalRecords",out var total)||total<0||!root.TryGetProperty("business",out var business)||!UniqueObject(business)||!root.TryGetProperty("customers",out var array)||array.ValueKind!=JsonValueKind.Array)return null;
  var count=array.GetArrayLength();
  if(count!=Math.Min(size,Math.Max(0,total-skip)))return null;
  var rows=new List<AccountingCustomer>(count);var ids=new HashSet<Guid>();
  foreach(var row in array.EnumerateArray())
  {
   if(!UniqueObject(row)||!Text(row,"key",64,out var key)||!Guid.TryParse(key,out var id)||id==Guid.Empty||!ids.Add(id)||!Text(row,"name",512,out var name)||string.IsNullOrWhiteSpace(name)||!Text(row,"status",128,out var status))return null;
   rows.Add(new(id,name,status));
  }
  var schema=string.Join(Environment.NewLine,business.EnumerateObject().Take(24).Select(p=>$"{Label(p.Name)}: {Type(p.Value)}"));
  if(business.EnumerateObject().Count()>24)schema+=Environment.NewLine+"Additional fields omitted.";
  string? businessName=Text(business,"name",512,out var bname)?bname:null;
  Guid? candidate=Text(business,"key",64,out var bkey)&&Guid.TryParse(bkey,out var bid)&&bid!=Guid.Empty?bid:null;
  return new(skip,size,total,rows.AsReadOnly(),schema,businessName,candidate);
 }
}
