using PracticeManager.Core;
using System.Text.Json;
internal static class AccountingCustomerPageTests
{
 public static async Task Run(Action<bool,string> check)
 {
  var probe=new ManagerDesktopProbe();
  var fixture=Guid.NewGuid().ToString("N");var id1=Guid.NewGuid();var id2=Guid.NewGuid();
  object Row(Guid id,string name)=>new { key=id,name,status="Active",accountsReceivable=new { amount=123.45 },attachment=false };
  string Page(object[] rows,int skip=0,int size=20,int total=2)=>JsonSerializer.Serialize(new {business=new {key=Guid.NewGuid(),name=fixture},skip,pageSize=size,totalRecords=total,customers=rows});
  await AccountingProbeTests.WithServer(200,Page(new[]{Row(id1,fixture),Row(id2,fixture)}),async(url,observed)=>{
   var r=await probe.ReadCustomerPageAsync(url,fixture,0);
   check(r.Success&&r.CustomerPage!=null,"customer page parsed");
   var p=r.CustomerPage!;
   check(p.Customers.Count==2&&p.Customers[0].Key==id1&&p.Customers[1].Key==id2,"distinct UUIDs retained despite duplicate names");
   check(p.Customers[0].Name==fixture&&p.Customers[0].Status=="Active","observed name and status mapped");
   check(p.Skip==0&&p.PageSize==20&&p.TotalRecords==2,"customer pagination metadata mapped");
   check(p.BusinessSchema.Contains("key: String (UUID-shaped)")&&p.BusinessSchema.Contains("name: String"),"business object structure inspected");
   check(!p.BusinessSchema.Contains(fixture)&&!r.ToString().Contains(fixture)&&!r.ToString().Contains(id1.ToString())&&!p.BusinessSchema.Contains("123.45"),"customer result diagnostics omit values");
   check((await observed).StartsWith("GET /api2/customers?skip=0&pageSize=20 HTTP/1.1",StringComparison.Ordinal),"bounded customer page GET");
  });
  await AccountingProbeTests.WithServer(200,Page(new[]{Row(id1,"A")},20,20,21),async(url,observed)=>{
   var r=await probe.ReadCustomerPageAsync(url,fixture,20);
   check(r.Success&&r.CustomerPage?.Skip==20,"second page parsed");
   check((await observed).StartsWith("GET /api2/customers?skip=20&pageSize=20 HTTP/1.1",StringComparison.Ordinal),"second page request offset");
  });
  await AccountingProbeTests.WithServer(200,Page(Array.Empty<object>(),0,20,0),async(url,_)=>check((await probe.ReadCustomerPageAsync(url,fixture,0)).CustomerPage?.Customers.Count==0,"empty customer register supported"));
  foreach(var body in new[]{
   "{}",
   Page(new[]{Row(id1,"A"),Row(id1,"B")}),
   Page(new object[]{new {key="not-a-uuid",name="A",status="Active"}},0,20,1),
   Page(new object[]{new {key=Guid.Empty,name="A",status="Active"}},0,20,1),
   Page(new object[]{new {key=id1,name=123,status="Active"}},0,20,1),
   Page(new object[]{new {key=id1,name="A"}},0,20,1),
   Page(new[]{Row(id1,"A")},1,20,2),
   Page(new[]{Row(id1,"A")},0,20,0),
   Page(new[]{Row(id1,"A")},0,20,2),
   Page(Enumerable.Range(0,21).Select(n=>Row(Guid.NewGuid(),"A")).ToArray(),0,20,21),
   Page(new[]{Row(id1,"A")},0,50,1)
  }) await AccountingProbeTests.WithServer(200,body,async(url,_)=>{var r=await probe.ReadCustomerPageAsync(url,fixture,0);check(!r.Success&&r.CustomerPage==null,"malformed customer page rejected atomically");});
  check((await probe.ReadCustomerPageAsync("http://127.0.0.1:1/api2",fixture,-1)).Outcome=="InvalidPage","negative page offset rejected");
  check((await probe.ReadCustomerPageAsync("http://example.invalid:12345/api2",fixture,0)).Outcome=="InvalidEndpoint","customer reader external host rejected");
  await AccountingProbeTests.WithServer(401,"{}",async(url,_)=>check((await probe.ReadCustomerPageAsync(url,fixture,0)).Outcome=="Unauthorized","customer reader authentication error"));
 }
}
