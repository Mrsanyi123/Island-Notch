using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace Island.Google;
internal sealed class GoogleAuth
{
    private string access="";
    private DateTimeOffset expires;
    private readonly SemaphoreSlim gate=new(1,1);
    public bool Connected=>new SecretStore().Get("GoogleRefresh").Length>0;
    private static string Random()=>Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
    public async Task Connect()
    {
        var secrets=new SecretStore();string id=secrets.Get("GoogleClientId"),secret=secrets.Get("GoogleClientSecret");if(id.Length==0||secret.Length==0)throw new InvalidOperationException();
        string verifier=Random(),state=Random();string challenge=Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+','-').Replace('/','_');
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try {
            string redirect="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/";
            string scope="https://www.googleapis.com/auth/calendar.readonly https://www.googleapis.com/auth/gmail.readonly https://www.googleapis.com/auth/analytics.readonly";
            Api.Open("https://accounts.google.com/o/oauth2/v2/auth?client_id="+Uri.EscapeDataString(id)+"&redirect_uri="+Uri.EscapeDataString(redirect)+"&response_type=code&scope="+Uri.EscapeDataString(scope)+"&access_type=offline&prompt=consent&code_challenge_method=S256&code_challenge="+challenge+"&state="+state);
            using var client=await listener.AcceptTcpClientAsync(timeout.Token);using var stream=client.GetStream();using var reader=new System.IO.StreamReader(stream,Encoding.ASCII,false,1024,true);
            string line=await reader.ReadLineAsync(timeout.Token)??"";string[] parts=line.Split(' ');if(parts.Length<2)throw new InvalidOperationException();
            var query=System.Web.HttpUtility.ParseQueryString(new Uri("http://127.0.0.1"+parts[1]).Query);bool valid=query["state"]==state&&!string.IsNullOrEmpty(query["code"]);
            byte[] response=Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nConnection: close\r\n\r\n"+(valid?"Return to Island to finish signing in.":"Sign-in rejected. Return to Island."));await stream.WriteAsync(response,timeout.Token);
            if(!valid)throw new InvalidOperationException();await Token(new(){{"code",query["code"]!},{"client_id",id},{"client_secret",secret},{"redirect_uri",redirect},{"grant_type","authorization_code"},{"code_verifier",verifier}});
        } finally {listener.Stop();}
    }
    private async Task Token(Dictionary<string,string> fields)
    {
        using var res=await Api.Client.PostAsync("https://oauth2.googleapis.com/token",new System.Net.Http.FormUrlEncodedContent(fields));res.EnsureSuccessStatusCode();using var doc=JsonDocument.Parse(await res.Content.ReadAsStringAsync());var root=doc.RootElement;
        access=root.GetProperty("access_token").GetString()!;expires=DateTimeOffset.Now.AddSeconds(root.GetProperty("expires_in").GetInt32()-60);if(root.TryGetProperty("refresh_token",out var refresh))new SecretStore().Set("GoogleRefresh",refresh.GetString()!);
    }
    public async Task<string> Access() {await gate.WaitAsync();try{if(access.Length>0&&expires>DateTimeOffset.Now)return access;var s=new SecretStore();await Token(new(){{"client_id",s.Get("GoogleClientId")},{"client_secret",s.Get("GoogleClientSecret")},{"refresh_token",s.Get("GoogleRefresh")},{"grant_type","refresh_token"}});return access;}finally{gate.Release();}}
    public void Disconnect(){access="";expires=default;var secrets=new SecretStore();secrets.Remove("GoogleRefresh");secrets.Remove("GoogleEmail");}
}
