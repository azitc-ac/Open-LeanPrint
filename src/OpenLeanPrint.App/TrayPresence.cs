using System.Windows;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace OpenLeanPrint.App;

/// <summary>
/// The tray icon. It exists so the app can keep collecting captured jobs after
/// its window is closed — printing from another application should still land
/// in the pool, and reopening the window should not be a new start.
/// <para>
/// WPF has no tray icon of its own, so this wraps WinForms' NotifyIcon. All
/// WinForms types stay behind this class.
/// </para>
/// </summary>
internal sealed class TrayPresence : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _collectItem;
    private bool _suppressCollectEvent;

    public TrayPresence()
    {
        var showItem = new WinForms.ToolStripMenuItem("Show OpenLeanPrint");
        showItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);

        _collectItem = new WinForms.ToolStripMenuItem("Collect captured jobs") { CheckOnClick = true };
        _collectItem.CheckedChanged += (_, _) =>
        {
            if (!_suppressCollectEvent) CollectingChanged?.Invoke(this, _collectItem.Checked);
        };

        var exitItem = new WinForms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(showItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_collectItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "OpenLeanPrint",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The user asked for the window back.</summary>
    public event EventHandler? ShowRequested;

    /// <summary>The user asked to quit for real.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>The user toggled collecting from the tray menu.</summary>
    public event EventHandler<bool>? CollectingChanged;

    /// <summary>Mirrors the app's state into the menu without raising <see cref="CollectingChanged"/>.</summary>
    public void SetCollecting(bool collecting)
    {
        if (_collectItem.Checked == collecting) return;

        _suppressCollectEvent = true;
        _collectItem.Checked = collecting;
        _suppressCollectEvent = false;
    }

    /// <summary>Shows a balloon — used when something happens while the window is hidden.</summary>
    public void Notify(string title, string text) =>
        _icon.ShowBalloonTip(4000, title, text, WinForms.ToolTipIcon.Info);

    private static Drawing.Icon LoadIcon()
    {
        // The component form works no matter which assembly hosts the window.
        var uri = new Uri("pack://application:,,,/OpenLeanPrint;component/Assets/OpenLeanPrint.ico", UriKind.Absolute);
        using var stream = Application.GetResourceStream(uri)?.Stream
            ?? throw new InvalidOperationException("The application icon resource is missing.");
        return new Drawing.Icon(stream);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
