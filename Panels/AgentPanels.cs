using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Island;

internal sealed class CodexPanel : IslandPanel
{
    public override string Id => "codex";
    public override string Title => "Codex";
    public override string Glyph => "\uE943";
    public override int Order => 82;
    public override Color Accent => Ui.C("#64D2FF");
    private readonly AgentCard card = new("Codex", "\uE943", Ui.C("#64D2FF"), AgentUsage.Codex, "Codex finished");
    public override string? RestingText => card.Resting;
    public override void Start() => card.Refresh();
    public override void Tick() => card.Tick();
    public override void OnShown() => card.Refresh();
    protected override FrameworkElement Build() => card.Build();
}

internal sealed class CopilotPanel : IslandPanel
{
    public override string Id => "copilot";
    public override string Title => "Copilot";
    public override string Glyph => "\uE943";
    public override int Order => 84;
    public override Color Accent => Ui.C("#0A84FF");
    private readonly AgentCard card = new("Copilot", "\uE943", Ui.C("#0A84FF"), AgentUsage.Copilot, "Copilot finished");
    public override string? RestingText => card.Resting;
    public override void Start() => card.Refresh();
    public override void Tick() => card.Tick();
    public override void OnShown() => card.Refresh();
    protected override FrameworkElement Build() => card.Build();
}

internal sealed class AgentCard
{
    private readonly string name, glyph, alertTitle;
    private readonly Color accent;
    private readonly Func<AgentSnapshot> read;
    private TextBlock? value, project, detail;
    private AgentSnapshot snap = new();
    private DateTime last = DateTime.MinValue;
    private bool wasActive, alerted;
    public string? Resting => !snap.Found ? name + " · not found" : snap.TokensToday > 0 ? name + " " + AgentUsage.Compact(snap.TokensToday) + " tok" : snap.EventsToday > 0 ? name + " " + snap.EventsToday + " events" : name + " · quiet";

    public AgentCard(string name, string glyph, Color accent, Func<AgentSnapshot> read, string alertTitle)
    {
        this.name = name; this.glyph = glyph; this.accent = accent; this.read = read; this.alertTitle = alertTitle;
    }

    public FrameworkElement Build()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        value = Ui.Text("–", 36, Ui.Ink, FontWeights.SemiBold);
        project = Ui.Text(" ", 16, Ui.B(accent), FontWeights.SemiBold); project.Margin = new Thickness(0, 8, 0, 0);
        detail = Ui.Text(" ", 13.5, Ui.Dim); detail.TextWrapping = TextWrapping.Wrap; detail.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(Ui.Label("Today")); stack.Children.Add(value); stack.Children.Add(project); stack.Children.Add(detail);
        var root = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        root.Children.Add(Ui.Titled(name, glyph, stack, accent, Ui.Button("Refresh", accent, Refresh, 30), 22));
        Apply();
        return root;
    }

    public void Refresh() { snap = read(); last = DateTime.UtcNow; Note(); Apply(); }
    public void Tick()
    {
        if ((DateTime.UtcNow - last).TotalSeconds < 20) return;
        Refresh();
    }

    private void Note()
    {
        if (snap.Active) { wasActive = true; alerted = false; return; }
        if (wasActive && !alerted)
        {
            alerted = true; wasActive = false;
            Hub.Alert(alertTitle, snap.Project == "No project" ? "The run went quiet" : snap.Project, glyph, "#64D2FF");
        }
    }

    private void Apply()
    {
        if (value == null || project == null || detail == null) return;
        if (!snap.Found)
        {
            value.Text = "Not found";
            project.Text = " ";
            detail.Text = name + " logs are not on this PC. Island does not create the folder.";
            return;
        }
        value.Text = snap.TokensToday > 0 ? AgentUsage.Compact(snap.TokensToday) : snap.EventsToday > 0 ? snap.EventsToday.ToString() : "0";
        project.Text = snap.Project;
        detail.Text = snap.TokensToday > 0 ? "tokens today · local logs only" : snap.Status + " · local logs only";
    }
}
