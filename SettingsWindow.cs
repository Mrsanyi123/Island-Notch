using System;
using System.Windows;
using System.Windows.Controls;
namespace Island;
internal sealed class SettingsWindow : Window
{
    public static Action? Changed { get; set; }
    public static Func<System.Threading.Tasks.Task>? ConnectGoogle { get; set; }
    public static Action? DisconnectGoogle { get; set; }
    public static Func<string,string,System.Threading.Tasks.Task>? TestProvider { get; set; }
    public SettingsWindow()
    {
        Title="Island settings";Width=470;Height=720;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var secrets=new SecretStore();var settings=Settings.Load();var panel=new StackPanel { Margin=new Thickness(22) };Content=new ScrollViewer { Content=panel };
        var status=new TextBlock { Text=secrets.LoadError??"Credentials are encrypted for your Windows account.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12) };panel.Children.Add(status);
        void Label(string text)=>panel.Children.Add(new TextBlock {Text=text,Margin=new Thickness(0,10,0,4)});
        PasswordBox Secret(string name,string key) { Label(name);var box=new PasswordBox { Password=secrets.Get(key) };panel.Children.Add(box);return box; }
        var stripe=Secret("Stripe restricted key","Stripe");var polar=Secret("Polar organization token","Polar");
        foreach(var pair in new[]{("Stripe",stripe),("Polar",polar)}) { var b=new Button { Content="Test "+pair.Item1,Margin=new Thickness(0,5,0,0) }; b.Click+=async (_,_)=>{b.IsEnabled=false;try { if(TestProvider is null) status.Text="Provider available after revenue setup.";else { await TestProvider(pair.Item1,pair.Item2.Password);status.Text=pair.Item1+": connected"; } } catch(Exception ex) { status.Text=ex is System.Net.Http.HttpRequestException?"Connection failed. Check key, permissions, and network.":"Connection failed. Check settings."; } finally { b.IsEnabled=true; } };panel.Children.Add(b); }
        var client=Secret("Google desktop client ID","GoogleClientId");var secret=Secret("Google client secret","GoogleClientSecret");
        void SaveSecrets() { secrets=new SecretStore();secrets.Set("Stripe",stripe.Password.Trim());secrets.Set("Polar",polar.Password.Trim());secrets.Set("GoogleClientId",client.Password.Trim());secrets.Set("GoogleClientSecret",secret.Password.Trim()); }
        var connect=new Button { Content="Connect Google",Margin=new Thickness(0,8,0,0) };connect.Click+=async (_,_)=>{connect.IsEnabled=false;try {SaveSecrets();if(ConnectGoogle!=null) {await ConnectGoogle();status.Text="Google connected";}else status.Text="Google support is not ready yet.";}catch{status.Text="Google sign-in failed or was cancelled. Check your desktop OAuth client.";}finally{connect.IsEnabled=true;}};panel.Children.Add(connect);
        var disconnect=new Button { Content="Disconnect Google" };disconnect.Click+=(_,_)=>{DisconnectGoogle?.Invoke();status.Text="Google disconnected";};panel.Children.Add(disconnect);
        CheckBox Check(string text,bool value) {var c=new CheckBox { Content=text,IsChecked=value,Margin=new Thickness(0,5,0,0)};panel.Children.Add(c);return c;}
        var revenue=Check("Revenue card",settings.Revenue);var calendar=Check("Calendar card",settings.Calendar);var email=Check("Email card",settings.Email);
        var sales=Check("Sale alerts",settings.SaleAlerts);var meetings=Check("Meeting alerts",settings.MeetingAlerts);var emails=Check("Important email alerts",settings.EmailAlerts);
        TextBox Interval(string name,int value) {Label(name+" refresh (minutes, 1–60)");var t=new TextBox {Text=value.ToString()};panel.Children.Add(t);return t;}
        var ri=Interval("Revenue",settings.RevenueMinutes);var ci=Interval("Calendar",settings.CalendarMinutes);var ei=Interval("Email",settings.EmailMinutes);
        var save=new Button { Content="Save",Margin=new Thickness(0,18,0,0) };save.Click+=(_,_)=>{if(!int.TryParse(ri.Text,out int r)||!int.TryParse(ci.Text,out int c)||!int.TryParse(ei.Text,out int e)||r<1||r>60||c<1||c>60||e<1||e>60){status.Text="Enter intervals from 1 to 60.";return;}try {SaveSecrets();settings.RevenueMinutes=r;settings.CalendarMinutes=c;settings.EmailMinutes=e;settings.Revenue=revenue.IsChecked==true;settings.Calendar=calendar.IsChecked==true;settings.Email=email.IsChecked==true;settings.SaleAlerts=sales.IsChecked==true;settings.MeetingAlerts=meetings.IsChecked==true;settings.EmailAlerts=emails.IsChecked==true;settings.Save();Changed?.Invoke();Close();}catch{status.Text="Could not save settings. Check access to your AppData folder.";}};panel.Children.Add(save);
    }
}
