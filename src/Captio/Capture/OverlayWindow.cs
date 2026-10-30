using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Captio.Core.Annotations;
using Captio.Core.Settings;
using Captio.Interop;

namespace Captio.Capture;

internal enum Tool
{
    Select,
    Arrow,
    Redact,
}

/// <summary>
/// Frozen, dimmed image of one monitor where the user selects a region and annotates it.
/// All model coordinates are physical pixels relative to this monitor.
/// </summary>
internal sealed class OverlayWindow : Window
{
    private static readonly uint[] Palette = [0xFFE5484D, 0xFFF5A524, 0xFF30A46C, 0xFF3E8BFF, 0xFFF4F4F5];
    private static readonly double[] Thicknesses = [4, 6, 10];
    private const int MinRegionSize = 2;

    [Flags]
    private enum Edge { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

    private enum Drag { None, NewRegion, MoveRegion, ResizeRegion, DrawArrow, DrawRedaction, MoveAnnotation }

    private readonly CaptureSession _session;
    private readonly Surface _surface;
    private readonly Canvas _chrome = new();
    private readonly Border _toolbar;
    private readonly Dictionary<Tool, ToggleButton> _toolButtons = [];
    private readonly List<(uint Argb, ToggleButton Button)> _colorButtons = [];
    private readonly List<(double Value, ToggleButton Button)> _thicknessButtons = [];
    private Button _undoButton = null!, _redoButton = null!, _deleteButton = null!;

    private Int32Rect? _region;
    private Tool _tool = Tool.Arrow;
    private uint _color;
    private double _thickness;
    private int _selected = -1;

    private Drag _drag;
    private Edge _resizeEdges;
    private Point _dragStart;
    private Int32Rect _regionAtDragStart;
    private Annotation? _preview;

    public OverlayWindow(CaptureSession session, MonitorSnapshot snapshot, AppSettings settings)
    {
        _session = session;
        Snapshot = snapshot;
        _color = settings.ArrowArgb;
        _thickness = settings.ArrowThickness;
        Document.Changed += (_, _) => { _selected = -1; Refresh(); };

        Title = "Captio";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        Width = 1;
        Height = 1;

        _surface = new Surface(this);
        _toolbar = BuildToolbar();
        _toolbar.Visibility = Visibility.Collapsed;
        _chrome.Children.Add(_toolbar);

        var root = new Grid();
        root.Children.Add(_surface);
        root.Children.Add(_chrome);
        Content = root;

        SourceInitialized += (_, _) => ApplyBounds();
        _surface.MouseLeftButtonDown += OnMouseDown;
        _surface.MouseMove += OnMouseMove;
        _surface.MouseLeftButtonUp += OnMouseUp;
        KeyDown += OnKeyDown;
        UpdateToolbarState();
    }

    public MonitorSnapshot Snapshot { get; }

    public AnnotationDocument Document { get; } = new();

    public Int32Rect? Region => _region;

    public uint ArrowColor => _color;

    public double ArrowThickness => _thickness;

    private double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>Clears this monitor's selection when the user starts one elsewhere.</summary>
    public void ResetSelection()
    {
        _region = null;
        _drag = Drag.None;
        _preview = null;
        _selected = -1;
        _toolbar.Visibility = Visibility.Collapsed;
        Refresh();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        // WPF resizes the window on DPI changes; pin it back to the monitor.
        ApplyBounds();
    }

    private void ApplyBounds()
    {
        var b = Snapshot.Bounds;
        Native.SetWindowPos(new WindowInteropHelper(this).Handle, 0, b.X, b.Y, b.Width, b.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    // ---- Input ------------------------------------------------------------------------------

    private Point ToPixels(MouseEventArgs e)
    {
        var p = e.GetPosition(_surface);
        var s = Scale;
        return new Point(Math.Clamp(p.X * s, 0, Snapshot.Bounds.Width), Math.Clamp(p.Y * s, 0, Snapshot.Bounds.Height));
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var p = ToPixels(e);
        _dragStart = p;
        _surface.CaptureMouse();

        if (_region is not { } region)
        {
            BeginNewRegion();
            return;
        }

        _regionAtDragStart = region;
        var edges = HitHandles(region, p);
        var inside = Contains(region, p);

        if (_tool == Tool.Select)
        {
            var hit = Document.HitTest(p.X, p.Y, 6 * Scale);
            if (hit >= 0 && inside)
            {
                _selected = hit;
                _preview = Document.Items[hit];
                _drag = Drag.MoveAnnotation;
            }
            else if (edges != Edge.None)
            {
                _resizeEdges = edges;
                _drag = Drag.ResizeRegion;
                _selected = -1;
            }
            else if (inside)
            {
                _drag = Drag.MoveRegion;
                _selected = -1;
            }
            else
            {
                BeginNewRegion();
                return;
            }
        }
        else if (edges != Edge.None)
        {
            _resizeEdges = edges;
            _drag = Drag.ResizeRegion;
        }
        else if (inside)
        {
            _drag = _tool == Tool.Arrow ? Drag.DrawArrow : Drag.DrawRedaction;
        }
        else
        {
            BeginNewRegion();
            return;
        }

        if (_drag is Drag.MoveRegion or Drag.ResizeRegion) _toolbar.Visibility = Visibility.Collapsed;
        UpdateToolbarState();
        Refresh();
    }

    private void BeginNewRegion()
    {
        _session.OnSelectionStarted(this);
        _drag = Drag.NewRegion;
        _selected = -1;
        _region = new Int32Rect((int)_dragStart.X, (int)_dragStart.Y, 0, 0);
        _toolbar.Visibility = Visibility.Collapsed;
        Refresh();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var p = ToPixels(e);
        if (_drag == Drag.None)
        {
            _surface.Cursor = CursorAt(p);
            return;
        }

        var dx = p.X - _dragStart.X;
        var dy = p.Y - _dragStart.Y;
        switch (_drag)
        {
            case Drag.NewRegion:
                _region = FromCorners(_dragStart, p);
                break;
            case Drag.MoveRegion:
                var r = _regionAtDragStart;
                _region = new Int32Rect(
                    Math.Clamp(r.X + (int)dx, 0, Snapshot.Bounds.Width - r.Width),
                    Math.Clamp(r.Y + (int)dy, 0, Snapshot.Bounds.Height - r.Height),
                    r.Width, r.Height);
                break;
            case Drag.ResizeRegion:
                _region = Resize(_regionAtDragStart, _resizeEdges, dx, dy);
                break;
            case Drag.DrawArrow:
                _preview = new ArrowAnnotation(_dragStart.X, _dragStart.Y, p.X, p.Y, _color, _thickness);
                break;
            case Drag.DrawRedaction:
                _preview = RedactionAnnotation.FromCorners(_dragStart.X, _dragStart.Y, p.X, p.Y);
                break;
            case Drag.MoveAnnotation:
                _preview = Document.Items[_selected].Offset(dx, dy);
                break;
        }
        Refresh();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _surface.ReleaseMouseCapture();
        var drag = _drag;
        var preview = _preview;
        var selected = _selected;
        _drag = Drag.None;
        _preview = null;

        switch (drag)
        {
            case Drag.NewRegion when _region is { } r && (r.Width < MinRegionSize || r.Height < MinRegionSize):
                _region = null;
                break;
            case Drag.DrawArrow when preview is ArrowAnnotation a && a.Length >= 4 * Scale:
                Document.Add(a);
                break;
            case Drag.DrawRedaction when preview is RedactionAnnotation r && r.Width >= 2 && r.Height >= 2:
                Document.Add(r);
                break;
            case Drag.MoveAnnotation when preview is not null && preview != Document.Items[selected]:
                Document.Replace(selected, preview);
                _selected = selected;
                break;
        }

        if (_region is not null)
        {
            _toolbar.Visibility = Visibility.Visible;
            PositionToolbar();
        }
        UpdateToolbarState();
        Refresh();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var handled = true;
        switch (e.Key)
        {
            case Key.Escape: _session.Cancel(); break;
            case Key.Z when ctrl: Document.Undo(); break;
            case Key.Y when ctrl: Document.Redo(); break;
            case Key.C when ctrl: Deliver(Destination.Copy); break;
            case Key.S when ctrl: Deliver(Destination.Save); break;
            case Key.U when ctrl: Deliver(Destination.Imgur); break;
            case Key.Enter: Deliver(_session.DefaultDestination); break;
            case Key.Delete or Key.Back: DeleteSelected(); break;
            case Key.V when !ctrl: SetTool(Tool.Select); break;
            case Key.A when !ctrl: SetTool(Tool.Arrow); break;
            case Key.R when !ctrl: SetTool(Tool.Redact); break;
            default: handled = false; break;
        }
        e.Handled = handled;
    }

    private void Deliver(Destination destination)
    {
        if (_region is null || _drag != Drag.None) return;
        _session.Deliver(this, destination);
    }

    private void DeleteSelected()
    {
        if (_selected >= 0 && _selected < Document.Items.Count) Document.RemoveAt(_selected);
    }

    private void SetTool(Tool tool)
    {
        _tool = tool;
        if (tool != Tool.Select) _selected = -1;
        UpdateToolbarState();
        Refresh();
    }

    // ---- Geometry helpers ---------------------------------------------------------------------

    private static bool Contains(Int32Rect r, Point p) => p.X >= r.X && p.X <= r.X + r.Width && p.Y >= r.Y && p.Y <= r.Y + r.Height;

    private static Int32Rect FromCorners(Point a, Point b)
    {
        int x1 = (int)Math.Floor(Math.Min(a.X, b.X)), y1 = (int)Math.Floor(Math.Min(a.Y, b.Y));
        int x2 = (int)Math.Ceiling(Math.Max(a.X, b.X)), y2 = (int)Math.Ceiling(Math.Max(a.Y, b.Y));
        return new Int32Rect(x1, y1, x2 - x1, y2 - y1);
    }

    private Int32Rect Resize(Int32Rect r, Edge edges, double dx, double dy)
    {
        double left = r.X, top = r.Y, right = r.X + r.Width, bottom = r.Y + r.Height;
        if (edges.HasFlag(Edge.Left)) left += dx;
        if (edges.HasFlag(Edge.Right)) right += dx;
        if (edges.HasFlag(Edge.Top)) top += dy;
        if (edges.HasFlag(Edge.Bottom)) bottom += dy;
        var w = Snapshot.Bounds.Width;
        var h = Snapshot.Bounds.Height;
        var resized = FromCorners(new Point(Math.Clamp(left, 0, w), Math.Clamp(top, 0, h)), new Point(Math.Clamp(right, 0, w), Math.Clamp(bottom, 0, h)));
        return resized.Width < MinRegionSize || resized.Height < MinRegionSize ? r : resized;
    }

    private Edge HitHandles(Int32Rect r, Point p)
    {
        var reach = 7 * Scale;
        var edges = Edge.None;
        var withinX = p.X >= r.X - reach && p.X <= r.X + r.Width + reach;
        var withinY = p.Y >= r.Y - reach && p.Y <= r.Y + r.Height + reach;
        if (withinY && Math.Abs(p.X - r.X) <= reach) edges |= Edge.Left;
        if (withinY && Math.Abs(p.X - (r.X + r.Width)) <= reach) edges |= Edge.Right;
        if (withinX && Math.Abs(p.Y - r.Y) <= reach) edges |= Edge.Top;
        if (withinX && Math.Abs(p.Y - (r.Y + r.Height)) <= reach) edges |= Edge.Bottom;
        return edges;
    }

    private Cursor CursorAt(Point p)
    {
        if (_region is not { } r) return Cursors.Cross;
        if (_tool == Tool.Select && Contains(r, p) && Document.HitTest(p.X, p.Y, 6 * Scale) >= 0) return Cursors.SizeAll;
        return HitHandles(r, p) switch
        {
            Edge.Left or Edge.Right => Cursors.SizeWE,
            Edge.Top or Edge.Bottom => Cursors.SizeNS,
            Edge.Left | Edge.Top or Edge.Right | Edge.Bottom => Cursors.SizeNWSE,
            Edge.Right | Edge.Top or Edge.Left | Edge.Bottom => Cursors.SizeNESW,
            _ when _tool == Tool.Select && Contains(r, p) => Cursors.SizeAll,
            _ => Cursors.Cross,
        };
    }

    private void Refresh() => _surface.InvalidateVisual();

    // ---- Toolbar ------------------------------------------------------------------------------

    private Border BuildToolbar()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        _toolButtons[Tool.Select] = AddToggle(panel, Glyph(""), "Seleccionar y mover (V)", () => SetTool(Tool.Select));
        var arrowGlyph = Glyph("");
        arrowGlyph.RenderTransform = new RotateTransform(-45, 8, 8);
        _toolButtons[Tool.Arrow] = AddToggle(panel, arrowGlyph, "Flecha (A)", () => SetTool(Tool.Arrow));
        _toolButtons[Tool.Redact] = AddToggle(panel,
            new Rectangle { Width = 16, Height = 10, Fill = Brushes.Black, Stroke = (Brush)FindResource("Captio.Text"), StrokeThickness = 1.2 },
            "Censurar (R)", () => SetTool(Tool.Redact));
        panel.Children.Add(Separator());

        foreach (var argb in Palette)
        {
            var swatch = new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(AnnotationRenderer.ToColor(argb)) };
            _colorButtons.Add((argb, AddToggle(panel, swatch, "Color de flecha", () => { _color = argb; SetTool(Tool.Arrow); })));
        }
        panel.Children.Add(Separator());

        foreach (var value in Thicknesses)
        {
            var line = new Rectangle { Width = 16, Height = value / 2 + 1, RadiusX = 1, RadiusY = 1, Fill = (Brush)FindResource("Captio.Text") };
            _thicknessButtons.Add((value, AddToggle(panel, line, "Grosor de flecha", () => { _thickness = value; SetTool(Tool.Arrow); })));
        }
        panel.Children.Add(Separator());

        _undoButton = AddButton(panel, Glyph(""), "Deshacer (Ctrl+Z)", Document.Undo);
        _redoButton = AddButton(panel, Glyph(""), "Rehacer (Ctrl+Y)", Document.Redo);
        _deleteButton = AddButton(panel, Glyph(""), "Eliminar anotación (Supr)", DeleteSelected);
        panel.Children.Add(Separator());

        AddButton(panel, Glyph(""), "Copiar (Ctrl+C)", () => Deliver(Destination.Copy));
        AddButton(panel, Glyph(""), "Guardar en la biblioteca (Ctrl+S)", () => Deliver(Destination.Save));
        var upload = AddButton(panel, LabeledGlyph("", "Imgur"),
            _session.ImgurAvailable ? "Subir a Imgur (Ctrl+U)" : "Subida a Imgur no disponible: esta compilación no tiene Client-ID",
            () => Deliver(Destination.Imgur));
        upload.IsEnabled = _session.ImgurAvailable;
        ToolTipService.SetShowOnDisabled(upload, true);
        AddButton(panel, Glyph(""), "Cancelar (Esc)", _session.Cancel);

        return new Border
        {
            Child = panel,
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(12),
            Background = (Brush)FindResource("Captio.Bar"),
            BorderBrush = (Brush)FindResource("Captio.BarBorder"),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.45 },
        };
    }

    private static TextBlock Glyph(string glyph) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        FontSize = 16,
        Width = 16,
        Height = 16,
        TextAlignment = TextAlignment.Center,
    };

    private static StackPanel LabeledGlyph(string glyph, string label)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(Glyph(glyph));
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(6, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    private ToggleButton AddToggle(Panel panel, UIElement content, string tip, Action action)
    {
        var button = new ToggleButton { Content = content, ToolTip = tip, Focusable = false, Style = (Style)FindResource("Captio.BarToggle") };
        button.Click += (_, _) => { action(); UpdateToolbarState(); };
        panel.Children.Add(button);
        return button;
    }

    private Button AddButton(Panel panel, UIElement content, string tip, Action action)
    {
        var button = new Button { Content = content, ToolTip = tip, Focusable = false, Style = (Style)FindResource("Captio.BarButton") };
        button.Click += (_, _) => action();
        panel.Children.Add(button);
        return button;
    }

    private Border Separator() => new()
    {
        Width = 1,
        Margin = new Thickness(6, 6, 6, 6),
        Background = (Brush)FindResource("Captio.BarBorder"),
    };

    private void UpdateToolbarState()
    {
        foreach (var (tool, button) in _toolButtons) button.IsChecked = tool == _tool;
        foreach (var (argb, button) in _colorButtons) button.IsChecked = argb == _color;
        foreach (var (value, button) in _thicknessButtons) button.IsChecked = value == _thickness;
        _undoButton.IsEnabled = Document.CanUndo;
        _redoButton.IsEnabled = Document.CanRedo;
        _deleteButton.IsEnabled = _selected >= 0;
    }

    private void PositionToolbar()
    {
        if (_region is not { } r) return;
        var s = Scale;
        _toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _toolbar.DesiredSize;
        var width = ActualWidth;
        var height = ActualHeight;
        const double gap = 10;

        var left = Math.Clamp((r.X + r.Width) / s - size.Width, gap, Math.Max(gap, width - size.Width - gap));
        var below = (r.Y + r.Height) / s + gap;
        var above = r.Y / s - gap - size.Height;
        var top = below + size.Height <= height - gap ? below
            : above >= gap ? above
            : (r.Y + r.Height) / s - size.Height - gap;

        Canvas.SetLeft(_toolbar, left);
        Canvas.SetTop(_toolbar, top);
    }

    // ---- Rendering ----------------------------------------------------------------------------

    private sealed class Surface(OverlayWindow owner) : FrameworkElement
    {
        private static readonly Brush Dim = Frozen(new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0)));
        private static readonly Pen RegionPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x7C, 0x9C, 0xFF))), 1.5));
        private static readonly Pen SelectionPen = Frozen(new Pen(Brushes.White, 1.5) { DashStyle = DashStyles.Dash });
        private static readonly Brush HandleFill = Brushes.White;

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var bounds = owner.Snapshot.Bounds;
            var s = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var full = new Rect(0, 0, bounds.Width, bounds.Height);

            dc.PushTransform(new ScaleTransform(1 / s, 1 / s));
            dc.DrawImage(owner.Snapshot.Image, full);

            if (owner._region is not { } region)
            {
                dc.DrawRectangle(Dim, null, full);
                dc.Pop();
                return;
            }

            var r = new Rect(region.X, region.Y, region.Width, region.Height);
            dc.PushClip(new RectangleGeometry(r));
            var items = owner.Document.Items;
            for (var i = 0; i < items.Count; i++)
            {
                var item = i == owner._selected && owner._drag == Drag.MoveAnnotation ? owner._preview ?? items[i] : items[i];
                AnnotationRenderer.Draw(dc, item);
            }
            if (owner._preview is { } preview && owner._drag is Drag.DrawArrow or Drag.DrawRedaction) AnnotationRenderer.Draw(dc, preview);
            dc.Pop();

            dc.DrawGeometry(Dim, null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(r)));

            var pen = RegionPen.Clone();
            pen.Thickness = 1.5 * s;
            dc.DrawRectangle(null, pen, r);

            if (owner._selected >= 0 && owner._selected < items.Count && owner._drag != Drag.MoveAnnotation)
            {
                var bound = BoundsOf(items[owner._selected]);
                bound.Inflate(4 * s, 4 * s);
                var selectionPen = SelectionPen.Clone();
                selectionPen.Thickness = 1.5 * s;
                dc.DrawRectangle(null, selectionPen, bound);
            }

            var handle = 4 * s;
            foreach (var (hx, hy) in HandlePoints(r))
            {
                dc.DrawRectangle(HandleFill, pen, new Rect(hx - handle, hy - handle, handle * 2, handle * 2));
            }

            DrawSizeLabel(dc, r, s);
            dc.Pop();
        }

        private static IEnumerable<(double, double)> HandlePoints(Rect r)
        {
            var mx = r.X + r.Width / 2;
            var my = r.Y + r.Height / 2;
            return [(r.Left, r.Top), (mx, r.Top), (r.Right, r.Top), (r.Right, my), (r.Right, r.Bottom), (mx, r.Bottom), (r.Left, r.Bottom), (r.Left, my)];
        }

        private void DrawSizeLabel(DrawingContext dc, Rect r, double s)
        {
            var text = new FormattedText($"{(int)r.Width} × {(int)r.Height}", System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI Variable Text, Segoe UI"), 12 * s, Brushes.White, s);
            var pad = 5 * s;
            var y = r.Top - text.Height - pad * 2 - 6 * s;
            if (y < 0) y = r.Top + 6 * s;
            var box = new Rect(r.Left, y, text.Width + pad * 2, text.Height + pad);
            dc.DrawRoundedRectangle((Brush)owner.FindResource("Captio.Bar"), null, box, 6 * s, 6 * s);
            dc.DrawText(text, new Point(box.X + pad, box.Y + pad / 2));
        }

        private static Rect BoundsOf(Annotation annotation) => annotation switch
        {
            ArrowAnnotation a => new Rect(new Point(a.X1, a.Y1), new Point(a.X2, a.Y2)),
            RedactionAnnotation r => new Rect(r.X, r.Y, r.Width, r.Height),
            _ => Rect.Empty,
        };
    }
}
