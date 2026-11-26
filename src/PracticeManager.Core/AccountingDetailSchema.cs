using System.Text.Json;
using System.Text.RegularExpressions;
namespace PracticeManager.Core;
internal static class AccountingDetailSchema
{
 internal static AccountingProbeResult Describe(JsonElement root,AccountingDetailResource resource)
 {
  if(root.ValueKind!=JsonValueKind.Object)return new("InvalidDetailResponse",200);
  var fields=new SortedSet<string>(StringComparer.Ordinal);
  var truncated=false;int budget=240;
  string Type(JsonElement v)=>v.ValueKind==JsonValueKind.String&&Guid.TryParse(v.GetString(),out _)
   ?"String (UUID-shaped)":v.ValueKind is JsonValueKind.True or JsonValueKind.False?"Boolean":v.ValueKind.ToString();
  bool Walk(JsonElement node,string path,int depth)
  {
   if(--budget<0){truncated=true;return true;}
   if(node.ValueKind==JsonValueKind.Object){
    var names=new HashSet<string>(StringComparer.Ordinal);
    foreach(var p in node.EnumerateObject()){
     if(!names.Add(p.Name))return false;
     // Never emit dynamic custom-field dictionary keys, UUIDs, or arbitrary user-defined labels.
     if(!Regex.IsMatch(p.Name,@"\A[A-Za-z_][A-Za-z0-9_]{0,47}\z",RegexOptions.CultureInvariant)||Guid.TryParse(p.Name,out _)){truncated=true;continue;}
     if(path.Contains("custom",StringComparison.OrdinalIgnoreCase)||path.Contains("attachment",StringComparison.OrdinalIgnoreCase)){truncated=true;continue;}
     var name=path.Length==0?p.Name:path+"."+p.Name;
     if(fields.Count>=160){truncated=true;continue;}
     fields.Add(name+": "+Type(p.Value));
     if(depth<4){if(!Walk(p.Value,name,depth+1))return false;}
     else if(p.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)truncated=true;
    }
   }else if(node.ValueKind==JsonValueKind.Array){
    if(node.GetArrayLength()>2)truncated=true;
    foreach(var row in node.EnumerateArray().Take(2)){
     fields.Add(path+"[]: "+Type(row));
     if(depth<4&&!Walk(row,path+"[]",depth+1))return false;
    }
   }
   return true;
  }
  if(!Walk(root,"",0))return new("InvalidDetailResponse",200);
  var report=resource+" detail schema (field paths and types only)"+Environment.NewLine+string.Join(Environment.NewLine,fields);
  if(truncated)report+=Environment.NewLine+"Additional fields or samples omitted.";
  return new("Connected",200,"Object",report);
 }
}
