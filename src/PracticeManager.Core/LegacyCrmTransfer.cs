using System.Security.Cryptography;
using System.Text.Json;
namespace PracticeManager.Core;
public sealed record LegacyCrmClient(string LegacyId,string LegacyNumber,Client Value)
{public override string ToString()=>"[Legacy client]";}
public sealed record LegacyCrmContact(string LegacyId,string LegacyClientId,Contact Value)
{public override string ToString()=>"[Legacy contact]";}
public sealed record LegacyCrmPackage(string Sha256,IReadOnlyList<LegacyCrmClient> Clients,IReadOnlyList<LegacyCrmContact> Contacts,int DuplicateNameGroups,int DuplicateNumberGroups)
{public override string ToString()=>"[Confidential legacy CRM transfer]";}
public static class LegacyCrmTransfer
{
 public const int MaxBytes=10*1024*1024;
 private static void Exact(JsonElement item,params string[] names)
 {
  if(item.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Expected object.");
  var actual=item.EnumerateObject().Select(x=>x.Name).ToArray();
  if(actual.Length!=names.Length||!actual.ToHashSet(StringComparer.Ordinal).SetEquals(names))throw new ArgumentException("Unexpected, duplicate or missing fields.");
 }
 private static string Field(JsonElement item,string name,int max,bool required=false)
 {
  var field=item.GetProperty(name);if(field.ValueKind!=JsonValueKind.String)throw new ArgumentException("Expected text: "+name);
  var value=field.GetString()!;
  if(value.Any(ch=>char.IsControl(ch)&&ch is not ('\r' or '\n' or '\t')))throw new ArgumentException("Unsupported control character: "+name);
  return Validation.Text(value,max,name,required);
 }
 private static string Identifier(JsonElement item,string name)
 {
  var value=Field(item,name,100,true);
  if(value!=item.GetProperty(name).GetString()||value.Any(ch=>!char.IsAsciiLetterOrDigit(ch)&&ch is not ('.' or '_' or '-')))throw new ArgumentException("Invalid legacy identifier: "+name);
  return value;
 }
 public static LegacyCrmPackage Parse(byte[] data)
 {
  if(data==null||data.Length is <1 or >MaxBytes)throw new ArgumentException("Transfer must be between 1 byte and 10 MiB.");
  var copy=data.ToArray();var hash=Convert.ToHexString(SHA256.HashData(copy));
  try{
   using var json=JsonDocument.Parse(copy,new JsonDocumentOptions{MaxDepth=8});
   var root=json.RootElement;Exact(root,"format","clients","contacts");
   if(Field(root,"format",40,true)!="CRC-Legacy-CRM-v1")throw new ArgumentException("Unsupported transfer format.");
   var cl=root.GetProperty("clients");var co=root.GetProperty("contacts");
   if(cl.ValueKind!=JsonValueKind.Array||co.ValueKind!=JsonValueKind.Array||cl.GetArrayLength() is <1 or >5000||co.GetArrayLength()>10000)throw new ArgumentException("Transfer row count outside supported limits.");
   var clients=new List<LegacyCrmClient>();var contacts=new List<LegacyCrmContact>();
   var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var contactIds=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var primary=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var x in cl.EnumerateArray()){
    try{
     Exact(x,"legacyId","legacyNumber","type","company","first","last","email","phone","address1","address2","city","state","zip","notes","status");
     var id=Identifier(x,"legacyId");if(!ids.Add(id))throw new ArgumentException("Duplicate client identifier.");
     var type=Field(x,"type",80,true);var status=Field(x,"status",20,true);
     if(!Validation.ClientTypes.Contains(type)||status is not ("Active" or "Inactive"))throw new ArgumentException("Unmapped client type or status.");
     var company=Field(x,"company",150);var first=Field(x,"first",60);var last=Field(x,"last",60);
     if(company.Length+first.Length+last.Length==0)throw new ArgumentException("Client name required.");
     var client=new Client(0,"",type,company,first,last,Field(x,"email",150),Validation.Phone(Field(x,"phone",40)),Field(x,"address1",150),Field(x,"address2",150),Field(x,"city",80),Field(x,"state",30),Field(x,"zip",15),Field(x,"notes",20000),status);
     clients.Add(new(id,Field(x,"legacyNumber",100),client));
    }catch(ArgumentException e){throw new ArgumentException("Client row "+(clients.Count+1)+": "+e.Message);}
   }
   foreach(var x in co.EnumerateArray()){
    try{
     Exact(x,"legacyId","legacyClientId","first","last","title","email","phone","primary");
     var id=Identifier(x,"legacyId");var parent=Identifier(x,"legacyClientId");
     if(!contactIds.Add(id))throw new ArgumentException("Duplicate contact identifier.");
     if(!ids.Contains(parent))throw new ArgumentException("Contact parent missing.");
     var first=Field(x,"first",60);var last=Field(x,"last",60);
     if(first.Length+last.Length==0)throw new ArgumentException("Contact name required.");
     var flag=x.GetProperty("primary");if(flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new ArgumentException("Primary flag must be boolean.");
     var isPrimary=flag.GetBoolean();if(isPrimary&&!primary.Add(parent))throw new ArgumentException("Multiple primary contacts for one client.");
     contacts.Add(new(id,parent,new Contact(0,0,first,last,Field(x,"title",80),Field(x,"email",150),Validation.Phone(Field(x,"phone",40)),isPrimary)));
    }catch(ArgumentException e){throw new ArgumentException("Contact row "+(contacts.Count+1)+": "+e.Message);}
   }
   int dupNames=clients.GroupBy(x=>x.Value.DisplayName,StringComparer.OrdinalIgnoreCase).Count(g=>g.Count()>1);
   int dupNumbers=clients.Where(x=>x.LegacyNumber.Length>0).GroupBy(x=>x.LegacyNumber,StringComparer.OrdinalIgnoreCase).Count(g=>g.Count()>1);
   return new(hash,clients.AsReadOnly(),contacts.AsReadOnly(),dupNames,dupNumbers);
  }catch(JsonException){throw new ArgumentException("Invalid UTF-8 JSON transfer.");}
 }
}
