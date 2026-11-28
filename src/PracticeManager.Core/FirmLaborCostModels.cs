namespace PracticeManager.Core;
public sealed record FirmLaborCostRow(string Category,long ApprovedMinutes,long MissingRateMinutes,int MissingRateRecords,decimal KnownCost)
{
 public decimal? CompleteCost=>MissingRateRecords==0?KnownCost:null;
}
public sealed record FirmLaborCost(DateOnly Start,DateOnly End,IReadOnlyList<FirmLaborCostRow> Rows)
{
 public long ApprovedMinutes=>Rows.Sum(x=>x.ApprovedMinutes);
 public long MissingRateMinutes=>Rows.Sum(x=>x.MissingRateMinutes);
 public int MissingRateRecords=>Rows.Sum(x=>x.MissingRateRecords);
 public decimal KnownCost=>Rows.Sum(x=>x.KnownCost);
 public decimal? CompleteCost=>MissingRateRecords==0?KnownCost:null;
}
