using System.Drawing;
using System.Windows.Forms;

namespace Captio;

internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIcon(Action capture, Action library, Action settings, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Capturar imagen", null, (_, _) => capture());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Biblioteca", null, (_, _) => library());
        menu.Items.Add("Configuración", null, (_, _) => settings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => exit());

        _icon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "Captio",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) capture();
        };
    }

    public void Notify(string message) => _icon.ShowBalloonTip(3000, "Captio", message, ToolTipIcon.None);

    /// <summary>The app's icon at the tray's size, from the same file as the executable and the installer.</summary>
    private static Icon CreateIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Captio;component/Assets/Captio.ico"));
        using var stream = resource.Stream;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
