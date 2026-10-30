#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false
// Generates src/Captio/Assets/Captio.ico from the app's mark (viewfinder corners and a dot on an
// indigo tile), drawn crisply at every size Windows asks for. Run: dotnet run scripts/make-icon.cs
using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
var output = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".", "..", "src", "Captio", "Assets", "Captio.ico"));
Directory.CreateDirectory(Path.GetDirectoryName(output)!);

var frames = sizes.Select(Draw).ToList();
using (var file = File.Create(output))
{
    // ICONDIR, then one ICONDIRENTRY per image, then the PNG-encoded images.
    Span<byte> header = stackalloc byte[6];
    BinaryPrimitives.WriteUInt16LittleEndian(header[2..], 1);
    BinaryPrimitives.WriteUInt16LittleEndian(header[4..], (ushort)frames.Count);
    file.Write(header);

    var offset = 6 + 16 * frames.Count;
    Span<byte> entry = stackalloc byte[16];
    for (var i = 0; i < frames.Count; i++)
    {
        entry.Clear();
        entry[0] = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
        entry[1] = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1);   // planes
        BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 32);  // bits per pixel
        BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)frames[i].Length);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)offset);
        file.Write(entry);
        offset += frames[i].Length;
    }
    foreach (var frame in frames) file.Write(frame);
}
Console.WriteLine($"Wrote {output} ({string.Join(", ", sizes)} px)");

static byte[] Draw(int size)
{
    // The design is laid out on a 32-unit grid, the same as the tray icon.
    var s = size / 32f;
    using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bitmap))
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        var radius = 6f * s;
        var inset = 1f * s;
        var extent = size - 2 * inset;
        using var tile = new GraphicsPath();
        tile.AddArc(inset, inset, radius * 2, radius * 2, 180, 90);
        tile.AddArc(inset + extent - radius * 2, inset, radius * 2, radius * 2, 270, 90);
        tile.AddArc(inset + extent - radius * 2, inset + extent - radius * 2, radius * 2, radius * 2, 0, 90);
        tile.AddArc(inset, inset + extent - radius * 2, radius * 2, radius * 2, 90, 90);
        tile.CloseFigure();
        using var fill = new SolidBrush(Color.FromArgb(0x4F, 0x5B, 0xD5));
        g.FillPath(fill, tile);

        // Thicker strokes at tiny sizes so the corners stay legible.
        var stroke = Math.Max(1.6f, 3f * s);
        using var pen = new Pen(Color.White, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, [new PointF(8 * s, 14 * s), new PointF(8 * s, 8 * s), new PointF(14 * s, 8 * s)]);
        g.DrawLines(pen, [new PointF(18 * s, 24 * s), new PointF(24 * s, 24 * s), new PointF(24 * s, 18 * s)]);
        using var dot = new SolidBrush(Color.White);
        var d = Math.Max(2f, 4f * s);
        g.FillEllipse(dot, 16 * s - d / 2, 16 * s - d / 2, d, d);
    }
    using var png = new MemoryStream();
    bitmap.Save(png, ImageFormat.Png);
    return png.ToArray();
}
