using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
namespace Island.Google;
internal record Mail(string Id,string Sender,string Subject,DateTimeOffset Time,bool Important);
internal sealed class GmailService(GoogleAuth auth)
{
    private string history="";
    private readonly Dictionary<string,Mail> cache=new();
    private readonly HashSet<string> alerted=new();
    public int Unread {get;private set;}
    public List<Mail> Recent=>cache.Values.OrderByDescending(m=>m.Time).Take(5).ToList();
    public List<Mail> NewImportant {get;}=new();
    public void Reset(){history="";cache.Clear();alerted.Clear();Unread=0;NewImportant.Clear();}
    public async Task Fetch()
    {
        string token=await auth.Access();NewImportant.Clear();bool initial=history.Length==0;string checkpoint="";
        if(initial) {using var profile=await Api.Get("https://gmail.googleapis.com/gmail/v1/users/me/profile",token);checkpoint=Api.Text(profile.RootElement,"historyId");new SecretStore().Set("GoogleEmail",Api.Text(profile.RootElement,"emailAddress"));}
        bool changed=initial;var ids=new HashSet<string>();
        if(!initial)try {
            string page="";do{using var h=await Api.Get("https://gmail.googleapis.com/gmail/v1/users/me/history?startHistoryId="+history+page,token);var root=h.RootElement;checkpoint=Api.Text(root,"historyId");
                if(root.TryGetProperty("history",out var entries))foreach(var entry in entries.EnumerateArray()){changed=true;foreach(var type in new[]{"messagesAdded","labelsAdded"})if(entry.TryGetProperty(type,out var additions))foreach(var item in additions.EnumerateArray())ids.Add(Api.Text(item.GetProperty("message"),"id"));}
                page=root.TryGetProperty("nextPageToken",out var p)?"&pageToken="+Uri.EscapeDataString(p.GetString()!):"";
            }while(page.Length>0);
        }catch(HttpRequestException ex)when(ex.StatusCode==HttpStatusCode.NotFound){history="";await Fetch();return;}
        if(changed){
            using var label=await Api.Get("https://gmail.googleapis.com/gmail/v1/users/me/labels/INBOX",token);Unread=label.RootElement.GetProperty("messagesUnread").GetInt32();
            using var list=await Api.Get("https://gmail.googleapis.com/gmail/v1/users/me/messages?q=is%3Aunread%20in%3Ainbox&maxResults=5",token);var recentIds=new List<string>();if(list.RootElement.TryGetProperty("messages",out var messages))foreach(var m in messages.EnumerateArray())recentIds.Add(Api.Text(m,"id"));
            foreach(var id in recentIds.Concat(ids).Distinct()) {
                try{using var doc=await Api.Get("https://gmail.googleapis.com/gmail/v1/users/me/messages/"+id+"?format=metadata&metadataHeaders=From&metadataHeaders=Subject&fields=id,internalDate,labelIds,payload(headers)",token);var root=doc.RootElement;var labels=root.GetProperty("labelIds").EnumerateArray().Select(x=>x.GetString()).ToHashSet();if(!labels.Contains("INBOX"))continue;
                    string Header(string name)=>root.GetProperty("payload").GetProperty("headers").EnumerateArray().Where(h=>Api.Text(h,"name").Equals(name,StringComparison.OrdinalIgnoreCase)).Select(h=>Api.Text(h,"value")).FirstOrDefault()??"";
                    var mail=new Mail(id,Header("From"),Header("Subject"),DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(root.GetProperty("internalDate").GetString()!,System.Globalization.CultureInfo.InvariantCulture)),labels.Contains("IMPORTANT"));
                    if(!initial&&ids.Contains(id)&&mail.Important&&alerted.Add(id))NewImportant.Add(mail);if(initial&&mail.Important)alerted.Add(id);if(recentIds.Contains(id)&&labels.Contains("UNREAD"))cache[id]=mail;
                }catch(HttpRequestException ex)when(ex.StatusCode==HttpStatusCode.NotFound){}
            }
            foreach(var id in cache.Keys.Where(id=>!recentIds.Contains(id)).ToList())cache.Remove(id);
        }
        history=checkpoint;
    }
}
