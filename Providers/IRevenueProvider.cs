using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
namespace Island.Providers;
internal record Sale(string Id,string Provider,string Currency,decimal Amount,string Description,DateTimeOffset Time);
internal record RevenueSummary(IReadOnlyList<Sale> Sales)
{
    public string Totals(Func<Sale,bool> filter) { var groups=Sales.Where(filter).GroupBy(s=>s.Currency).Select(g=>Money.Format(g.Sum(s=>s.Amount),g.Key));return string.Join(" · ",groups.DefaultIfEmpty("—")); }
    public string Today=>Totals(s=>s.Time.LocalDateTime.Date==DateTime.Today);
    public string Month=>Totals(s=>s.Time.LocalDateTime>=new DateTime(DateTime.Today.Year,DateTime.Today.Month,1));
    public string Thirty=>Totals(s=>s.Time>=DateTimeOffset.Now.AddDays(-30));
}
internal interface IRevenueProvider
{
    string Name {get;}
    Task TestConnectionAsync();
    Task<RevenueSummary> FetchSummaryAsync();
    Task<IReadOnlyList<Sale>> FetchRecentSalesAsync();
}
internal static class Money
{
    public static decimal Convert(long amount,string currency) => amount/(currency.ToUpperInvariant() switch {"BIF" or "CLP" or "DJF" or "GNF" or "JPY" or "KMF" or "KRW" or "MGA" or "PYG" or "RWF" or "UGX" or "VND" or "VUV" or "XAF" or "XOF" or "XPF"=>1m,"BHD" or "JOD" or "KWD" or "OMR" or "TND"=>1000m,_=>100m});
    public static string Format(decimal amount,string currency)=>currency.ToUpperInvariant()+" "+amount.ToString("0.##");
}
