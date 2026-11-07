using System;
using System.Windows;
using System.Windows.Media;
namespace Island;

/// <summary>
/// One page of the expanded island (shown under the header, above the dock). Panels are discovered by reflection:
/// any non-abstract subclass with a public parameterless constructor is added to the dock, ordered by <see cref="Order"/>.
/// Constructors MUST be cheap (no I/O, no timers) — do real work in <see cref="Start"/> or <see cref="Build"/>.
/// </summary>
internal abstract class IslandPanel
{
    private FrameworkElement? view;
    /// <summary>Stable lowercase id, e.g. "revenue".</summary>
    public abstract string Id { get; }
    /// <summary>Title shown in the header and as the dock tooltip.</summary>
    public abstract string Title { get; }
    /// <summary>One Segoe Fluent Icons / Segoe MDL2 Assets glyph for the dock.</summary>
    public abstract string Glyph { get; }
    /// <summary>Dock position (Home 0, Revenue 10, Analytics 20 ...).</summary>
    public abstract int Order { get; }
    /// <summary>Panel accent colour (dock highlight, charts, active pills).</summary>
    public virtual Color Accent => Ui.C("#0A84FF");

    /// <summary>The panel content, built lazily on first show. It is laid out at exactly Ui.ContentW x Ui.ContentH.</summary>
    public FrameworkElement View => view ??= Build();
    public bool IsBuilt => view != null;
    protected abstract FrameworkElement Build();

    /// <summary>Called once after the window handle exists (even if the island has never been expanded). Start listeners / trackers here.</summary>
    public virtual void Start() { }
    /// <summary>Called every second on the UI thread, whether or not the panel is visible. Keep it very cheap.</summary>
    public virtual void Tick() { }
    /// <summary>The panel just became visible (or the island expanded while it was current): refresh data / UI.</summary>
    public virtual void OnShown() { }
    /// <summary>The panel was hidden (another panel chosen or island collapsed).</summary>
    public virtual void OnHidden() { }
    /// <summary>Short text (max ~28 chars) for the collapsed notch if the user pins this panel as the resting state; null = panel cannot be pinned.</summary>
    public virtual string? RestingText => null;
    /// <summary>Live task that should appear in the closed notch without being pinned.</summary>
    public virtual string? ActiveTaskText => null;
}
