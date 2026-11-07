using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
namespace Island.Providers;
internal abstract class RevenueProvider(string key) : IRevenueProvider
{
    protected string Key=>key;
    public abstract string Name {get;}
    protected abstract Task<IReadOnlyList<Sale>> Fetch(bool test);
    public async Task TestConnectionAsync()=>await Fetch(true);
    public async Task<RevenueSummary> FetchSummaryAsync()=>new(await Fetch(false));
    public async Task<IReadOnlyList<Sale>> FetchRecentSalesAsync()=>(await Fetch(false)).OrderByDescending(s=>s.Time).Take(5).ToList();
    protected static DateTimeOffset Since {get {var month=new DateTimeOffset(new DateTime(DateTime.Today.Year,DateTime.Today.Month,1));return month<DateTimeOffset.Now.AddDays(-30)?month:DateTimeOffset.Now.AddDays(-30);}}
}
internal sealed class StripeProvider(string key) : RevenueProvider(key)
{
    public override string Name=>"Stripe";
    protected override async Task<IReadOnlyList<Sale>> Fetch(bool test)
    {
        var sales=new List<Sale>();string cursor="";
        do {
            using var doc=await Api.Get("https://api.stripe.com/v1/charges?limit="+(test?1:100)+"&created[gte]="+Since.ToUnixTimeSeconds()+cursor,Key);
            var root=doc.RootElement;var data=root.GetProperty("data");
            foreach(var c in data.EnumerateArray()) { if(c.GetProperty("paid").GetBoolean() && c.GetProperty("status").GetString()=="succeeded") {string currency=c.GetProperty("currency").GetString()!;sales.Add(new Sale(c.GetProperty("id").GetString()!,Name,currency,Money.Convert(c.GetProperty("amount").GetInt64()-c.GetProperty("amount_refunded").GetInt64(),currency),Api.Text(c,"description","Sale"),DateTimeOffset.FromUnixTimeSeconds(c.GetProperty("created").GetInt64()))); } }
            if(test || !root.GetProperty("has_more").GetBoolean() || data.GetArrayLength()==0) break;
            cursor="&starting_after="+data[data.GetArrayLength()-1].GetProperty("id").GetString();
        } while(true);
        return sales;
    }
}
internal sealed class PolarProvider(string key) : RevenueProvider(key)
{
    public override string Name=>"Polar";
    protected override async Task<IReadOnlyList<Sale>> Fetch(bool test)
    {
        var sales=new List<Sale>();int page=1;
        do {
            using var doc=await Api.Get("https://api.polar.sh/v1/orders/?limit="+(test?1:100)+"&sorting=-created_at&page="+page,Key);
            var root=doc.RootElement;var items=root.GetProperty("items");bool older=false;
            foreach(var o in items.EnumerateArray()) {var time=o.GetProperty("created_at").GetDateTimeOffset();if(time<Since){older=true;continue;}if(!o.GetProperty("paid").GetBoolean())continue;string currency=o.GetProperty("currency").GetString()!;long amount=o.GetProperty("total_amount").GetInt64()-(o.TryGetProperty("refunded_amount",out var refund)?refund.GetInt64():0);string description=o.TryGetProperty("product",out var p)&&p.ValueKind==System.Text.Json.JsonValueKind.Object?Api.Text(p,"name","Sale"):Api.Text(o,"billing_name","Sale");sales.Add(new Sale(o.GetProperty("id").GetString()!,Name,currency,Money.Convert(amount,currency),description,time));}
            if(test||older||items.GetArrayLength()<100||page>=root.GetProperty("pagination").GetProperty("max_page").GetInt32())break;page++;
        } while(true);
        return sales;
    }
}
