using System.Text.Json;
namespace PracticeManager.Core;
public static class BusinessMarker
{
 public const string TestName="CRC Integration Identity Test";
 public static bool ValidName(string? name)=>name==TestName;
 static bool Unique(JsonElement v)=>v.ValueKind==JsonValueKind.Object&&v.EnumerateObject().Select(x=>x.Name).Distinct(StringComparer.Ordinal).Count()==v.EnumerateObject().Count();
 static bool Int(JsonElement v,string field,out int n){n=0;return v.TryGetProperty(field,out var p)&&p.ValueKind==JsonValueKind.Number&&p.TryGetInt32(out n);}
 internal static AccountingProbeResult Parse(JsonElement root,string name)
 {
  if(!Unique(root)||!Int(root,"skip",out var skip)||skip!=0||!Int(root,"pageSize",out var size)||size<1||size>2||!Int(root,"totalRecords",out var total)||total<0||!root.TryGetProperty("textCustomFields",out var rows)||rows.ValueKind!=JsonValueKind.Array)return new("InvalidMarkerResponse",200);
  if(total>size)return new("MarkerAmbiguous",200);
  if(rows.GetArrayLength()!=total)return new("InvalidMarkerResponse",200);
  Guid? match=null;
  foreach(var row in rows.EnumerateArray()){
   if(!Unique(row)||!row.TryGetProperty("name",out var n)||n.ValueKind!=JsonValueKind.String||!row.TryGetProperty("key",out var k)||k.ValueKind!=JsonValueKind.String||!Guid.TryParse(k.GetString(),out var id)||id==Guid.Empty)return new("InvalidMarkerResponse",200);
   if(n.GetString()!=name)continue;
   if(match.HasValue)return new("MarkerAmbiguous",200);
   match=id;
  }
  return match.HasValue?new("Connected",200,"Object",MarkerKey:match):new("MarkerMissing",200);
 }
}
