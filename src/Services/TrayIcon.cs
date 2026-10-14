using System;
using System.Drawing;
using System.Windows.Forms;

namespace USBBlockManager.Services;

/// <summary>
/// Иконка в системном трее с контекстным меню.
/// Живёт независимо от главного окна.
/// </summary>
public class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    /// <summary>Двойной клик по иконке.</summary>
    public event EventHandler? DoubleClicked;

    /// <summary>Пункт «Открыть».</summary>
    public event EventHandler? OpenRequested;

    /// <summary>Пункт «Выход».</summary>
    public event EventHandler? ExitRequested;

    public TrayIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Visible = true,
            Text = "USB Block Manager",
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => DoubleClicked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Показать balloon-уведомление.</summary>
    public void ShowBalloon(string title, string text, int timeoutMs = 3000)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(timeoutMs, title, text, ToolTipIcon.Info);
        }
        catch { }
    }

    public void Dispose()
    {
        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        catch { }
    }
}