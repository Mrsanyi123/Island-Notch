using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Island.Providers;
namespace Island;
internal sealed class Dashboard
{
    public event Action? Changed;
    public void RaiseChanged()=>Changed?.Invoke();
    public event Action<string>? Alert;
    public string RevenueText {get;private set;}="Open Settings to connect";
    public string RevenueBreakdown {get;private set;}="";
    public bool RevenueConnected {get;private set;}
    public string RevenueToday {get;private set;}="—";
    public string RevenueMonth {get;private set;}="—";
    public string RevenueThirty {get;private set;}="—";
    public string RevenueErrorText {get;private set;}="";
    public List<string> RevenueRecent {get;private set;}=new();
    public string RevenueActivity {get;private set;}="Revenue • connect in Settings";
    private readonly Dictionary<string,List<Sale>> cache=new();
    private readonly Dictionary<string,DateTimeOffset> next=new();
    private readonly Dictionary<string,int> failures=new();
    private readonly Dictionary<string,string> revenueErrors=new();
    private readonly DispatcherTimer timer=new() { Interval=TimeSpan.FromSeconds(30) };
    private bool busy;
    public Dashboard() { SettingsWindow.TestProvider=async (name,key)=>await Provider(name,key).TestConnectionAsync();SettingsWindow.Changed=()=>{cache.Clear();next.Clear();_ = Refresh();};timer.Tick+=async (_,_)=>await Refresh();timer.Start(); }
    private static IRevenueProvider Provider(string name,string key)=>name=="Stripe"?new StripeProvider(key):new PolarProvider(key);
    private async Task Revenue(bool force)
    {
        var settings=Settings.Load();if(!settings.Revenue)return;var secrets=new SecretStore();var errors=new List<string>();
        foreach(var name in new[]{"Stripe","Polar"}) {
            string key=secrets.Get(name);if(key.Length==0){cache.Remove(name);revenueErrors.Remove(name);continue;}
            if(!force&&next.GetValueOrDefault(name)>DateTimeOffset.Now)continue;
            try { var result=await Provider(name,key).FetchSummaryAsync();if(cache.TryGetValue(name,out var old)&&settings.SaleAlerts){var ids=old.Select(s=>s.Id).ToHashSet();foreach(var sale in result.Sales.Where(s=>!ids.Contains(s.Id)&&s.Amount>0).OrderBy(s=>s.Time))Alert?.Invoke("+"+Money.Format(sale.Amount,sale.Currency)+" via "+name);}cache[name]=result.Sales.ToList();revenueErrors.Remove(name);failures[name]=0;next[name]=DateTimeOffset.Now.AddMinutes(settings.RevenueMinutes); }
            catch(Exception ex){revenueErrors[name]=name+": "+Api.Error(ex);int count=Math.Min(failures.GetValueOrDefault(name)+1,6);failures[name]=count;next[name]=DateTimeOffset.Now.AddMinutes(Math.Min(60,Math.Pow(2,count)));}
        }
        errors.AddRange(revenueErrors.Values);var summary=new RevenueSummary(cache.Values.SelectMany(s=>s).ToList());RevenueActivity=summary.Today+" today";RevenueConnected=cache.Count>0;RevenueToday=summary.Today;RevenueMonth=summary.Month;RevenueThirty=summary.Thirty;RevenueRecent=summary.Sales.OrderByDescending(s=>s.Time).Take(3).Select(s=>Money.Format(s.Amount,s.Currency)+" · "+s.Description+" · "+Age(s.Time)).ToList();RevenueErrorText=string.Join("\n",errors);
        RevenueBreakdown=string.Join("\n",cache.Select(p=>p.Key+": "+new RevenueSummary(p.Value).Today));
        RevenueText=cache.Count==0&&errors.Count==0?"Open Settings to connect":"Today  "+summary.Today+"\nMonth  "+summary.Month+"\n30 days  "+summary.Thirty+"\n\n"+string.Join("\n",summary.Sales.OrderByDescending(s=>s.Time).Take(5).Select(s=>Money.Format(s.Amount,s.Currency)+" · "+s.Description+" · "+Age(s.Time)))+"\n"+string.Join("\n",errors);
    }
    public static string Age(DateTimeOffset time)=>Math.Max(0,(int)(DateTimeOffset.Now-time).TotalMinutes)+"m ago";
    public async Task Refresh(bool force=false) {if(busy)return;busy=true;try{await Revenue(force);await RefreshGoogle(force);Changed?.Invoke();}finally{busy=false;}}
    public readonly Google.GoogleAuth Auth=new();
    private Google.GmailService? gmail;
    public Google.GmailService Gmail=>gmail??=new(Auth);
    public string EmailError {get;private set;}="";
    public List<Google.Meeting> Meetings {get;private set;}=new();
    /// <summary>Events for the next 7 days (up to 60).</summary>
    public List<Google.Meeting> Agenda {get;private set;}=new();
    /// <summary>Every paid sale of the last ~30 days from all connected providers.</summary>
    public List<Sale> Sales=>cache.Values.SelectMany(s=>s).OrderByDescending(s=>s.Time).ToList();
    public string CalendarError {get;private set;}="";
    private readonly HashSet<string> meetingAlerts=new();
    private async Task RefreshGoogle(bool force)
    {
        var settings=Settings.Load();if(!Auth.Connected)return;
        if(settings.Calendar&&(force||next.GetValueOrDefault("Calendar")<=DateTimeOffset.Now))try{Agenda=await new Google.CalendarService(Auth).Fetch(7,60);Meetings=Agenda.Where(m=>m.Start<DateTimeOffset.Now.Date.AddDays(2)).Take(5).ToList();CalendarError="";next["Calendar"]=DateTimeOffset.Now.AddMinutes(settings.CalendarMinutes);failures["Calendar"]=0;}catch(Exception ex){CalendarError=Api.Error(ex);Backoff("Calendar");}
        if(settings.Email&&(force||next.GetValueOrDefault("Email")<=DateTimeOffset.Now))try{await Gmail.Fetch();EmailError="";next["Email"]=DateTimeOffset.Now.AddMinutes(settings.EmailMinutes);failures["Email"]=0;if(settings.EmailAlerts)foreach(var mail in Gmail.NewImportant)Alert?.Invoke("Important email: "+mail.Subject);}catch(Exception ex){EmailError=Api.Error(ex);Backoff("Email");}
        CheckMeetings();
    }
    public void CheckMeetings() {if(!Settings.Load().MeetingAlerts)return;foreach(var m in Meetings.Where(m=>!m.AllDay&&m.Start>DateTimeOffset.Now&&m.Start<=DateTimeOffset.Now.AddMinutes(5)))if(meetingAlerts.Add(m.Id+m.Start.ToString("o")))Alert?.Invoke(m.Title+" starts in "+Math.Ceiling((m.Start-DateTimeOffset.Now).TotalMinutes)+"m");}
    private void Backoff(string name) {int count=Math.Min(failures.GetValueOrDefault(name)+1,6);failures[name]=count;next[name]=DateTimeOffset.Now.AddMinutes(Math.Min(60,Math.Pow(2,count)));}
    public void Stop()=>timer.Stop();
}
