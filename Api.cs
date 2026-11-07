using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
namespace Island;
internal static class Api
{
    internal static readonly HttpClient Client=new() { Timeout=TimeSpan.FromSeconds(30) };
    public static async Task<JsonDocument> Get(string url,string token) {using var req=new HttpRequestMessage(HttpMethod.Get,url);req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);using var res=await Client.SendAsync(req);if(!res.IsSuccessStatusCode)throw new HttpRequestException("API request failed",null,res.StatusCode);return JsonDocument.Parse(await res.Content.ReadAsStringAsync());}
    public static string Text(JsonElement e,string key,string fallback="")=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??fallback:fallback;
    public static string Error(Exception ex)=>ex is HttpRequestException h?h.StatusCode switch {System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.BadRequest=>"invalid key, expired token or permissions — open Settings",System.Net.HttpStatusCode.TooManyRequests=>"rate limited — retrying later",_=>"offline or service unavailable — retrying"}:"Unable to refresh — open Settings";
    public static void Open(string url) {if(Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme=="https")try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url){UseShellExecute=true});}catch{}}
}
