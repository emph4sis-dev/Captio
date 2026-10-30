using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Captio;

internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIcon(Action capture, Action clip, Action library, Action settings, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Capturar imagen", null, (_, _) => capture());
        menu.Items.Add("Grabar clip", null, (_, _) => clip());
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

    /// <summary>Viewfinder corners on an indigo tile, drawn at runtime.</summary>
    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var tile = new GraphicsPath();
            tile.AddArc(1, 1, 12, 12, 180, 90);
            tile.AddArc(19, 1, 12, 12, 270, 90);
            tile.AddArc(19, 19, 12, 12, 0, 90);
            tile.AddArc(1, 19, 12, 12, 90, 90);
            tile.CloseFigure();
            using var fill = new SolidBrush(Color.FromArgb(0x4F, 0x5B, 0xD5));
            g.FillPath(fill, tile);

            using var pen = new Pen(Color.White, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, [new PointF(8, 14), new PointF(8, 8), new PointF(14, 8)]);
            g.DrawLines(pen, [new PointF(18, 24), new PointF(24, 24), new PointF(24, 18)]);
            using var dot = new SolidBrush(Color.White);
            g.FillEllipse(dot, 14, 14, 4, 4);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
