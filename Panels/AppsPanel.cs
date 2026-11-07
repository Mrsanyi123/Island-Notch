using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Island;

/// <summary>A launcher for the apps available inside Island.</summary>
internal sealed class AppsPanel : IslandPanel
{
    private readonly IReadOnlyList<IslandPanel> apps;
    private readonly Action<IslandPanel> open;

    public AppsPanel(IReadOnlyList<IslandPanel> apps, Action<IslandPanel> open)
    {
        this.apps = apps;
        this.open = open;
    }

    public override string Id => "apps";
    public override string Title => "Apps";
    public override string Glyph => "\uE80A";
    public override int Order => int.MaxValue;

    protected override FrameworkElement Build()
    {
        var root = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        var caption = Ui.Text("Choose an app to open in Island", 13, Ui.Dim);
        caption.Margin = new Thickness(4, 0, 0, 12); root.Children.Add(caption);

        var tiles = new WrapPanel();
        foreach (var app in apps)
        {
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = new Border
            {
                Width = 38, Height = 38, CornerRadius = new CornerRadius(12),
                Background = Ui.B(app.Accent, 0x26), Child = Ui.Glyph(app.Glyph, 20, Ui.B(app.Accent))
            };
            content.Children.Add(icon);
            var name = Ui.Text(app.Title, 14, Ui.Ink, FontWeights.SemiBold);
            name.VerticalAlignment = VerticalAlignment.Center; name.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(name, 1); content.Children.Add(name);

            var tile = Ui.Card(content, 12, 16);
            tile.Width = 212; tile.Height = 70; tile.Margin = new Thickness(4, 0, 4, 8);
            tile.Cursor = Cursors.Hand; tile.ToolTip = "Open " + app.Title;
            var normalFill = tile.Background;
            tile.MouseEnter += (_, _) => { tile.Background = Ui.ChipHover; tile.BorderBrush = Ui.B(app.Accent, 0x80); };
            tile.MouseLeave += (_, _) => { tile.Background = normalFill; tile.BorderBrush = Ui.Line; };
            tile.MouseLeftButtonUp += (_, e) => { open(app); e.Handled = true; };
            tiles.Children.Add(tile);
        }

        var scroll = Ui.Scroll(tiles); Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        return root;
    }
}
