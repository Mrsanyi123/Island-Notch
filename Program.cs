using System;
using System.Threading;
using System.Windows;
using Forms = System.Windows.Forms;
namespace Island;
internal static class Program
{
    [STAThread] public static int Main(string[] args)
    {
        if(Array.IndexOf(args,"--self-test")>=0)return SelfTest.Run();
        int pv=Array.IndexOf(args,"--preview");
        using var mutex = new Mutex(true, "Local\\Island.Desktop", out bool first);
        if (pv<0&&!first) return 0;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new IslandWindow();
        if (pv>=0 && pv+1<args.Length) { window.Loaded+=(_,_)=>window.RunPreview(args[pv+1]); }
        using var tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "Island", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => window.OpenSettings());
        menu.Items.Add("Refresh now", null, (_, _) => window.RefreshNow());
        var pause=new Forms.ToolStripMenuItem("Pause alerts") { CheckOnClick=true };pause.CheckedChanged+=(_,_)=>window.PauseAlerts=pause.Checked;menu.Items.Add(pause);
        var startup=new Forms.ToolStripMenuItem("Start with Windows") {Checked=Startup.Enabled};startup.Click+=(_,_)=>{try{Startup.Set(!startup.Checked);startup.Checked=Startup.Enabled;}catch(Exception ex){MessageBox.Show(ex is InvalidOperationException?ex.Message:"Could not update your Windows startup setting.","Island");}};menu.Items.Add(startup);
        menu.Items.Add("Exit", null, (_, _) => { window.Close(); app.Shutdown(); });
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => window.OpenSettings();
        app.Run(window);
        return 0;
    }
}
