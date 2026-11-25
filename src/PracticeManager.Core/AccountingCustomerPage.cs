namespace PracticeManager.Core;
public sealed record AccountingCustomer(Guid Key,string Name,string Status)
{
 public override string ToString()=>"[Customer]";
}
public sealed record AccountingCustomerPage(int Skip,int PageSize,int TotalRecords,IReadOnlyList<AccountingCustomer> Customers,string BusinessSchema,string? BusinessName=null,Guid? BusinessKey=null)
{
 public override string ToString()=>$"Customer page: {Customers.Count} rows";
}
