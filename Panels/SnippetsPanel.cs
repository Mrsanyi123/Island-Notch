using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Island;

public sealed class SnippetItem
{
    public string Trigger { get; set; } = "";
    public string Body { get; set; } = "";
}
public sealed class SnippetData
{
    public bool Enabled { get; set; }
    public string Excluded { get; set; } = "";
    public List<SnippetItem> Items { get; set; } = new();
}

/// <summary>
/// Expands a short trigger into saved text. The keyboard hook exists only while this is on.
/// Keystrokes are matched in a tiny memory buffer and are never written to disk or logs.
/// </summary>
internal sealed class SnippetsPanel : IslandPanel
{
    public override string Id => "snippets";
    public override string Title => "Snippets";
    public override string Glyph => "\uE8C1";
    public override int Order => 135;
    public override Color Accent => Ui.C("#BF5AF2");

    private SnippetData data = new();
    private StackPanel? list;
    private TextBox? triggerBox, bodyBox, excludeBox;

    public override void Start()
    {
        data = Store.Load<SnippetData>("snippets");
        data.Items ??= new();
        if (data.Enabled) SnippetHook.Install(data);
        var window = Application.Current?.MainWindow;
        if (window != null) window.Closed += (_, _) => SnippetHook.Uninstall();
    }

    protected override FrameworkElement Build()
    {
        var root = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        var editor = new StackPanel();
        editor.Children.Add(Ui.Text("Trigger", 12.5, Ui.Dim));
        triggerBox = Ui.Input("sig"); triggerBox.Margin = new Thickness(0, 4, 0, 8); editor.Children.Add(triggerBox);
        editor.Children.Add(Ui.Text("Expands to", 12.5, Ui.Dim));
        bodyBox = Ui.Input("Use {date} and {time}", true); bodyBox.Height = 90; bodyBox.Margin = new Thickness(0, 4, 0, 8); editor.Children.Add(bodyBox);
        editor.Children.Add(Ui.Button("Save snippet", Accent, Save, 32));
        var note = Ui.Text("The hook is removed when snippets are off or Island exits. It does not keep what you type.", 12.5, Ui.Dim);
        note.TextWrapping = TextWrapping.Wrap; note.Margin = new Thickness(0, 10, 0, 0); editor.Children.Add(note);
        root.Children.Add(Ui.Card(editor, 16, 16));

        list = new StackPanel();
        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        right.RowDefinitions.Add(new RowDefinition());
        var tools = new StackPanel();
        var enable = Ui.Button(data.Enabled ? "Snippets on" : "Snippets off", Accent, Toggle, 30);
        enable.Name = "enable";
        enable.HorizontalAlignment = HorizontalAlignment.Left;
        tools.Children.Add(enable);
        tools.Children.Add(Ui.Text("Skip these apps (process names, commas)", 12.5, Ui.Dim));
        excludeBox = Ui.Input("code, devenv"); excludeBox.Text = data.Excluded; excludeBox.Margin = new Thickness(0, 4, 0, 0);
        excludeBox.LostFocus += (_, _) => { data.Excluded = excludeBox.Text.Trim(); Store.Save("snippets", data); SnippetHook.Update(data); };
        tools.Children.Add(excludeBox);
        right.Children.Add(tools);
        var scroll = Ui.Scroll(list); Grid.SetRow(scroll, 2); right.Children.Add(scroll);
        var card = Ui.Titled("Your triggers", "\uE8C1", right, Accent, null, 14);
        Grid.SetColumn(card, 2); root.Children.Add(card);
        Render();
        return root;
    }

    private void Toggle()
    {
        data.Enabled = !data.Enabled;
        Store.Save("snippets", data);
        if (data.Enabled) SnippetHook.Install(data); else SnippetHook.Uninstall();
        if (IsBuilt) BuildToggle();
    }

    private void BuildToggle()
    {
        // The enable button is recreated with the list header text by a full refresh of its label via the visual tree.
        if (list?.Parent is not ScrollViewer scroll || scroll.Parent is not Grid grid) return;
        if (grid.Children[0] is StackPanel tools && tools.Children[0] is Border box && box.Child is TextBlock label)
        {
            label.Text = data.Enabled ? "Snippets on" : "Snippets off";
            box.Background = data.Enabled ? Ui.B(Accent, 0x44) : Ui.B(Accent, 0x2A);
        }
    }

    private void Save()
    {
        string trigger = (triggerBox?.Text ?? "").Trim();
        string body = bodyBox?.Text ?? "";
        if (trigger.Length < 2 || trigger.Length > 16 || body.Trim().Length == 0) return;
        if (trigger.Any(char.IsWhiteSpace)) return;
        data.Items.RemoveAll(i => i.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase));
        data.Items.Insert(0, new SnippetItem { Trigger = trigger, Body = body.Trim() });
        Store.Save("snippets", data);
        SnippetHook.Update(data);
        if (triggerBox != null) triggerBox.Text = "";
        if (bodyBox != null) bodyBox.Text = "";
        Render();
    }

    private void Render()
    {
        if (list == null) return;
        list.Children.Clear();
        if (data.Items.Count == 0) { list.Children.Add(Ui.Text("No snippets yet.", 14, Ui.Dim)); return; }
        foreach (var item in data.Items)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var col = new StackPanel();
            col.Children.Add(Ui.Text(item.Trigger, 14.5, Ui.B(Accent), FontWeights.SemiBold));
            var body = Ui.Text(item.Body.Replace("\n", " "), 12.5, Ui.Dim); col.Children.Add(body);
            var remove = Ui.Button("Remove", Ui.C("#FF453A"), () => { data.Items.Remove(item); Store.Save("snippets", data); SnippetHook.Update(data); Render(); }, 26);
            Grid.SetColumn(remove, 1); row.Children.Add(col); row.Children.Add(remove);
            list.Children.Add(row);
        }
    }
}

internal static class SnippetHook
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private delegate nint Proc(int code, nint wParam, nint lParam);
    private static readonly Proc Callback = OnHook;
    private static nint hook;
    private static SnippetData data = new();
    private static readonly StringBuilder buffer = new();
    private static bool sending;

    public static void Install(SnippetData source)
    {
        data = source;
        if (hook != 0) return;
        hook = SetWindowsHookEx(WhKeyboardLl, Callback, GetModuleHandle(null), 0);
        buffer.Clear();
    }

    public static void Update(SnippetData source) => data = source;

    public static void Uninstall()
    {
        if (hook != 0) { UnhookWindowsHookEx(hook); hook = 0; }
        buffer.Clear();
    }

    private static nint OnHook(int code, nint wParam, nint lParam)
    {
        if (code < 0 || !data.Enabled || sending) return CallNextHookEx(hook, code, wParam, lParam);
        if (wParam != WmKeyDown) return CallNextHookEx(hook, code, wParam, lParam);
        var info = Marshal.PtrToStructure<Kbd>(lParam);
        if ((info.Flags & 0x10) != 0) return CallNextHookEx(hook, code, wParam, lParam);
        if (Excluded()) { buffer.Clear(); return CallNextHookEx(hook, code, wParam, lParam); }
        if (info.Vk == 0x08) { if (buffer.Length > 0) buffer.Length -= 1; return CallNextHookEx(hook, code, wParam, lParam); }
        if (info.Vk is 0x0D or 0x1B or 0x09) { buffer.Clear(); return CallNextHookEx(hook, code, wParam, lParam); }
        string typed = CharOf(info);
        if (typed.Length != 1) return CallNextHookEx(hook, code, wParam, lParam);
        buffer.Append(typed);
        if (buffer.Length > 32) buffer.Remove(0, buffer.Length - 32);
        foreach (var item in data.Items)
        {
            if (item.Trigger.Length < 2 || buffer.Length < item.Trigger.Length) continue;
            if (!buffer.ToString().EndsWith(item.Trigger, StringComparison.Ordinal)) continue;
            int prefix = item.Trigger.Length - 1;
            string text = Expand(item.Body);
            buffer.Clear();
            Application.Current?.Dispatcher.BeginInvoke(() => Type(prefix, text));
            return 1;
        }
        return CallNextHookEx(hook, code, wParam, lParam);
    }

    private static bool Excluded()
    {
        string name = Foreground();
        if (name.Length == 0) return false;
        if (name.Equals("Island", StringComparison.OrdinalIgnoreCase) || name.Equals("dotnet", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var part in (data.Excluded ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (name.Equals(part, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string Foreground()
    {
        try
        {
            nint hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == 0) return "";
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return "";
            return System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
        }
        catch { return ""; }
    }

    private static string Expand(string body) => body.Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd")).Replace("{time}", DateTime.Now.ToString("h:mm tt"));

    private static void Type(int backspaces, string text)
    {
        sending = true;
        try
        {
            for (int i = 0; i < backspaces; i++) Key(0x08);
            foreach (var ch in text) Unicode(ch);
        }
        catch { }
        finally { sending = false; buffer.Clear(); }
    }

    private static void Key(ushort vk) => Tap(vk);
    private static void Unicode(char ch)
    {
        Send(0, (ushort)ch, 0x0004);
        Send(0, (ushort)ch, 0x0004 | 0x0002);
    }
    private static void Tap(ushort vk)
    {
        Send(vk, 0, 0);
        Send(vk, 0, 0x0002);
    }
    private static void Send(ushort vk, ushort scan, uint flags)
    {
        var input = new Input { Type = 1, Union = new InputUnion { Ki = new KeyInput { Vk = vk, Scan = scan, Flags = flags } } };
        SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
    }

    private static string CharOf(Kbd info)
    {
        var state = new byte[256];
        if (!GetKeyboardState(state)) return "";
        var sb = new StringBuilder(4);
        int n = ToUnicode(info.Vk, info.Scan, state, sb, sb.Capacity, 0);
        return n == 1 ? sb.ToString() : "";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Kbd { public uint Vk, Scan, Flags, Time; public nint Extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyInput Ki;
        [FieldOffset(0)] public MousePad Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyInput { public ushort Vk, Scan; public uint Flags, Time; public nint Extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MousePad { public int Dx, Dy; public uint Data, Flags, Time; public nint Extra; }

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, Proc proc, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool GetKeyboardState(byte[] state);
    [DllImport("user32.dll")] private static extern int ToUnicode(uint vk, uint scan, byte[] state, StringBuilder buffer, int size, uint flags);
}
