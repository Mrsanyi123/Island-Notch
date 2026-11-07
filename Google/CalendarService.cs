using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
namespace Island.Google;
internal record Meeting(string Id,string Title,DateTimeOffset Start,string Url,string Join,bool AllDay);
internal sealed class CalendarService(GoogleAuth auth)
{
    public async Task<List<Meeting>> Fetch(int days=2,int max=5)
    {
        string start=Uri.EscapeDataString(new DateTimeOffset(DateTime.Today).ToString("o")),end=Uri.EscapeDataString(new DateTimeOffset(DateTime.Today.AddDays(days)).ToString("o"));
        using var doc=await Api.Get("https://www.googleapis.com/calendar/v3/calendars/primary/events?singleEvents=true&orderBy=startTime&maxResults=250&timeMin="+start+"&timeMax="+end,await auth.Access());var result=new List<Meeting>();
        foreach(var e in doc.RootElement.GetProperty("items").EnumerateArray()) {
            if(Api.Text(e,"status")=="cancelled")continue;var s=e.GetProperty("start");bool allDay=!s.TryGetProperty("dateTime",out var date);DateTimeOffset time=allDay?new DateTimeOffset(DateTime.Parse(s.GetProperty("date").GetString()!,System.Globalization.CultureInfo.InvariantCulture)):date.GetDateTimeOffset();if(!allDay&&time<DateTimeOffset.Now)continue;
            string join=Api.Text(e,"hangoutLink");if(join.Length==0 && e.TryGetProperty("conferenceData",out var conference)&&conference.TryGetProperty("entryPoints",out var entries))join=entries.EnumerateArray().Where(x=>Api.Text(x,"entryPointType")=="video").Select(x=>Api.Text(x,"uri")).FirstOrDefault()??"";
            if(join.Length==0){var match=Regex.Match(Api.Text(e,"location")+" "+Api.Text(e,"description"),@"https://(?:[\w.-]+\.)?zoom\.us/[^\s<>""']+");if(match.Success)join=match.Value;}
            result.Add(new Meeting(Api.Text(e,"id"),Api.Text(e,"summary","Untitled event"),time,Api.Text(e,"htmlLink"),join,allDay));
        }
        return result.OrderBy(x=>x.Start).Take(max).ToList();
    }
}
