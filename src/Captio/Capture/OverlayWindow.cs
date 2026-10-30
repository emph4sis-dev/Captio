using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Captio.Core.Annotations;
using Captio.Core.Layout;
using Captio.Core.Settings;
using Captio.Interop;

namespace Captio.Capture;

internal enum Tool
{
    Select,
    Shape,
    Redact,
}

/// <summary>
/// Frozen, dimmed image of one monitor where the user selects a region and annotates it.
/// All model coordinates are physical pixels relative to this monitor.
/// </summary>
internal sealed class OverlayWindow : Window
{
    private const int MinRegionSize = 2;

    private static readonly (ShapeKind Kind, string Name)[] ShapeNames =
    [
        (ShapeKind.Arrow, "Flecha"),
        (ShapeKind.Rectangle, "Rectángulo"),
        (ShapeKind.Ellipse, "Elipse"),
    ];

    [Flags]
    private enum Edge { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

    private enum Drag { None, NewRegion, MoveRegion, ResizeRegion, DrawShape, DrawRedaction, MoveAnnotation }

    private readonly CaptureSession _session;
    private readonly Surface _surface;
    private readonly Canvas _chrome = new();
    private readonly ToolbarSide _side;
    private readonly bool _vertical;
    private readonly double _strokeDip;
    private readonly Border _toolbar;
    private readonly Dictionary<Tool, ToggleButton> _toolButtons = [];
    private ToggleButton _shapeButton = null!;
    private Button _undoButton = null!, _redoButton = null!, _deleteButton = null!;
    private Ellipse _colorSwatch = null!;
    private Popup _shapePopup = null!, _colorPopup = null!;
    private readonly List<(ShapeKind Kind, Button Item)> _shapeItems = [];
    private readonly List<(uint Argb, Button Item)> _colorItems = [];
    private Popup? _openPopup;
    private int _popupIndex;
    private long _popupClosedAt;

    private Int32Rect? _region;
    // Selecting first lets the user adjust the region before choosing a tool.
    private Tool _tool = Tool.Select;
    private ShapeKind _shape;
    private uint _color;
    private int _selected = -1;

    private Drag _drag;
    private Edge _resizeEdges;
    private Point _dragStart;
    private Point _lastPoint;
    private Int32Rect _regionAtDragStart;
    private Annotation? _preview;

    public OverlayWindow(CaptureSession session, MonitorSnapshot snapshot, AppSettings settings)
    {
        _session = session;
        Snapshot = snapshot;
        _shape = settings.LastShape;
        _color = settings.ArrowArgb;
        _side = settings.ToolbarSide;
        _vertical = ToolbarPlacement.IsVertical(_side);
        _strokeDip = settings.StrokeThickness;
        Document.Changed += (_, _) => { _selected = -1; UpdateToolbarState(); Refresh(); };

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
        PreviewKeyDown += OnPreviewKeyDown;
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        UpdateToolbarState();
    }

    public MonitorSnapshot Snapshot { get; }

    public AnnotationDocument Document { get; } = new();

    public Int32Rect? Region => _region;

    private double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>Clears this monitor's selection when the user starts one elsewhere.</summary>
    public void ResetSelection()
    {
        _region = null;
        _drag = Drag.None;
        _preview = null;
        _selected = -1;
        _toolbar.Visibility = Visibility.Collapsed;
        ClosePopup();
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
        _lastPoint = p;
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
            _drag = _tool == Tool.Shape ? Drag.DrawShape : Drag.DrawRedaction;
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
        _lastPoint = p;
        if (_drag == Drag.None)
        {
            _surface.Cursor = CursorAt(p);
            return;
        }
        UpdateDrag(p);
    }

    private void UpdateDrag(Point p)
    {
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
            case Drag.DrawShape:
                var constrain = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                _preview = ShapeFactory.Create(_shape, _dragStart.X, _dragStart.Y, p.X, p.Y, constrain, _color, _strokeDip * Scale);
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
            case Drag.DrawShape or Drag.DrawRedaction when preview is not null && ShapeFactory.IsMeaningful(preview, 4 * Scale):
                Document.Add(preview);
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

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_openPopup is null) return;

        var items = _openPopup == _colorPopup ? _colorItems.Select(c => c.Item).ToList() : _shapeItems.Select(s => s.Item).ToList();
        var columns = _openPopup == _colorPopup ? 4 : 1;
        var handled = true;
        switch (e.Key)
        {
            case Key.Escape: ClosePopup(); break;
            case Key.Left: MovePopupFocus(items, -1); break;
            case Key.Right: MovePopupFocus(items, 1); break;
            case Key.Up: MovePopupFocus(items, -columns); break;
            case Key.Down: MovePopupFocus(items, columns); break;
            case Key.Tab: MovePopupFocus(items, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1); break;
            case Key.Enter or Key.Space: items[_popupIndex].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); break;
            default: handled = false; break;
        }
        e.Handled = handled;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var handled = true;
        switch (e.Key)
        {
            case Key.Escape: _session.Cancel(); break;
            case Key.X when ctrl: _session.Cancel(); break;
            case Key.Z when ctrl: Document.Undo(); break;
            case Key.Y when ctrl: Document.Redo(); break;
            case Key.C when ctrl: Deliver(Destination.Copy); break;
            case Key.S when ctrl: Deliver(Destination.Save); break;
            case Key.U when ctrl: Deliver(Destination.Imgur); break;
            case Key.Enter: Deliver(_session.DefaultDestination); break;
            case Key.Delete or Key.Back: DeleteSelected(); break;
            case Key.V when !ctrl: SetTool(Tool.Select); break;
            case Key.F when !ctrl && shift: OpenPopup(_shapePopup); break;
            case Key.F when !ctrl: SetTool(Tool.Shape); break;
            case Key.R when !ctrl: SetTool(Tool.Redact); break;
            case Key.C when !ctrl: OpenPopup(_colorPopup); break;
            case Key.LeftShift or Key.RightShift when _drag == Drag.DrawShape: UpdateDrag(_lastPoint); break;
            default: handled = false; break;
        }
        e.Handled = handled;
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        // Releasing Shift mid-drag turns the square or circle back into a free shape.
        if (e.Key is Key.LeftShift or Key.RightShift && _drag == Drag.DrawShape) UpdateDrag(_lastPoint);
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

    private void SetShape(ShapeKind shape)
    {
        _shape = shape;
        _session.RememberShape(shape);
        SetTool(Tool.Shape);
    }

    private void SetColor(uint argb)
    {
        _color = argb;
        _session.RememberColor(argb);
        UpdateToolbarState();
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
        var panel = new StackPanel { Orientation = _vertical ? Orientation.Vertical : Orientation.Horizontal };

        _toolButtons[Tool.Select] = AddToggle(panel, Glyph(""), "Seleccionar y mover", "V", () => SetTool(Tool.Select));
        BuildShapeButton(panel);
        _toolButtons[Tool.Redact] = AddToggle(panel,
            new Rectangle { Width = 16, Height = 10, Fill = Brushes.Black, Stroke = Res("Captio.Text"), StrokeThickness = 1.2 },
            "Censurar", "R", () => SetTool(Tool.Redact));
        panel.Children.Add(BuildColorButton());
        panel.Children.Add(Separator());

        _undoButton = AddButton(panel, Glyph(""), "Deshacer", "Ctrl+Z", Document.Undo);
        _redoButton = AddButton(panel, Glyph(""), "Rehacer", "Ctrl+Y", Document.Redo);
        _deleteButton = AddButton(panel, Glyph(""), "Eliminar anotación", "Supr", DeleteSelected);
        panel.Children.Add(Separator());

        AddButton(panel, Glyph(""), "Copiar", "Ctrl+C", () => Deliver(Destination.Copy));
        AddButton(panel, Glyph(""), "Guardar en la carpeta de capturas", "Ctrl+S", () => Deliver(Destination.Save));
        // The label keeps the one network action unambiguous where there is room for it.
        var upload = AddButton(panel, _vertical ? Glyph("") : LabeledGlyph("", "Imgur"),
            _session.ImgurAvailable ? "Subir a Imgur" : "Subida a Imgur no disponible: esta compilación no tiene Client-ID",
            _session.ImgurAvailable ? "Ctrl+U" : null, () => Deliver(Destination.Imgur));
        upload.IsEnabled = _session.ImgurAvailable;
        ToolTipService.SetShowOnDisabled(upload, true);
        AddButton(panel, Glyph(""), "Cancelar", "Ctrl+X", _session.Cancel);

        return new Border
        {
            Child = panel,
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(10),
            Background = Res("Captio.Bar"),
            BorderBrush = Res("Captio.BarBorder"),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.45 },
        };
    }

    /// <summary>Shows the current shape; clicking it opens the shape menu, like the color button.</summary>
    private void BuildShapeButton(Panel panel)
    {
        _shapeButton = AddToggle(panel, ShapeIcon(_shape), ShapeName(_shape), "F", () => OpenPopup(_shapePopup));
        _toolButtons[Tool.Shape] = _shapeButton;

        var menu = new StackPanel { Margin = new Thickness(2) };
        foreach (var (kind, name) in ShapeNames)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(ShapeIcon(kind));
            row.Children.Add(new TextBlock { Text = name, Margin = new Thickness(10, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center });
            var item = PopupItem(row, name, () => { SetShape(kind); ClosePopup(); });
            item.HorizontalContentAlignment = HorizontalAlignment.Left;
            item.Padding = new Thickness(8, 6, 8, 6);
            menu.Children.Add(item);
            _shapeItems.Add((kind, item));
        }
        _shapePopup = CreatePopup(menu, _shapeButton, "Formas");
    }

    private FrameworkElement BuildColorButton()
    {
        // Full-size palette glyph like every other icon, with a badge in the selected color.
        // The badge's ring uses the bar color so it reads as a cutout, not an outline.
        _colorSwatch = new Ellipse
        {
            Width = 8,
            Height = 8,
            Stroke = Res("Captio.Bar"),
            StrokeThickness = 1.5,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -2, -2),
        };
        var icon = new Grid { Width = 16, Height = 16, Children = { Glyph(""), _colorSwatch } };
        var button = new Button
        {
            Content = icon,
            ToolTip = "Color (C)", // updated with the color name
            Focusable = false,
            Margin = ButtonSpacing,
            Style = (Style)FindResource("Captio.BarButton"),
        };
        AutomationProperties.SetName(button, "Color");
        button.Click += (_, _) => OpenPopup(_colorPopup);

        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(2) };
        foreach (var color in ShapePalette.Colors)
        {
            var swatch = new Ellipse { Width = 18, Height = 18, Fill = new SolidColorBrush(AnnotationRenderer.ToColor(color.Argb)) };
            var item = PopupItem(swatch, color.Name, () => { SetColor(color.Argb); ClosePopup(); });
            grid.Children.Add(item);
            _colorItems.Add((color.Argb, item));
        }
        _colorPopup = CreatePopup(grid, button, "Colores");
        return button;
    }

    private Popup CreatePopup(UIElement content, UIElement target, string name)
    {
        var frame = new Border
        {
            Child = content,
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(10),
            Background = Res("Captio.Bar"),
            BorderBrush = Res("Captio.BarBorder"),
            BorderThickness = new Thickness(1),
        };
        AutomationProperties.SetName(frame, name);
        KeyboardNavigation.SetDirectionalNavigation(frame, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetTabNavigation(frame, KeyboardNavigationMode.Cycle);

        var popup = new Popup
        {
            Child = frame,
            PlacementTarget = target,
            Placement = !_vertical ? PlacementMode.Bottom : _side == ToolbarSide.Left ? PlacementMode.Right : PlacementMode.Left,
            StaysOpen = false,
            AllowsTransparency = true,
            HorizontalOffset = _vertical ? 0 : -4,
            VerticalOffset = _vertical ? -4 : 6,
        };
        popup.Closed += (_, _) =>
        {
            _popupClosedAt = Environment.TickCount64;
            if (_openPopup == popup) _openPopup = null;
            Keyboard.Focus(this);
        };
        return popup;
    }

    private void OpenPopup(Popup popup)
    {
        if (_region is null || _drag != Drag.None) return;
        // The click that closed the popup (StaysOpen=false) must not reopen it.
        if (Environment.TickCount64 - _popupClosedAt < 250) return;

        ClosePopup();
        _openPopup = popup;
        popup.IsOpen = true;

        var items = popup == _colorPopup ? _colorItems.Select(c => c.Item).ToList() : _shapeItems.Select(s => s.Item).ToList();
        var current = popup == _colorPopup
            ? _colorItems.FindIndex(c => c.Argb == _color)
            : _shapeItems.FindIndex(s => s.Kind == _shape);
        _popupIndex = Math.Max(0, current);
        HighlightPopupItem(items);
    }

    private void ClosePopup()
    {
        if (_openPopup is { } popup) popup.IsOpen = false;
        _openPopup = null;
    }

    private void MovePopupFocus(List<Button> items, int delta)
    {
        _popupIndex = ((_popupIndex + delta) % items.Count + items.Count) % items.Count;
        HighlightPopupItem(items);
    }

    private void HighlightPopupItem(List<Button> items)
    {
        PaintPopupItems();
        items[_popupIndex].Background = Res("Captio.Hover");
        items[_popupIndex].Focus();
    }

    /// <summary>The current color or shape keeps the "selected" background, with no outline.</summary>
    private void PaintPopupItems()
    {
        foreach (var (argb, item) in _colorItems) item.Background = argb == _color ? Res("Captio.Checked") : Brushes.Transparent;
        foreach (var (kind, item) in _shapeItems) item.Background = kind == _shape ? Res("Captio.Checked") : Brushes.Transparent;
    }

    private Button PopupItem(UIElement content, string name, Action choose)
    {
        var item = new Button { Content = content, ToolTip = name, Margin = new Thickness(1), Style = (Style)FindResource("Captio.BarButton") };
        AutomationProperties.SetName(item, name);
        item.Click += (_, _) => choose();
        return item;
    }

    private FrameworkElement ShapeIcon(ShapeKind kind)
    {
        FrameworkElement icon = kind switch
        {
            ShapeKind.Rectangle => new Rectangle { Width = 16, Height = 12, Stroke = Res("Captio.Text"), StrokeThickness = 1.8 },
            ShapeKind.Ellipse => new Ellipse { Width = 17, Height = 13, Stroke = Res("Captio.Text"), StrokeThickness = 1.8 },
            _ => Glyph("", rotate: -45),
        };
        return new Grid { Width = 18, Height = 16, Children = { icon } };
    }

    private static string ShapeName(ShapeKind kind) => ShapeNames.First(s => s.Kind == kind).Name;

    private static TextBlock Glyph(string glyph, double size = 16, double rotate = 0) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        FontSize = size,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        RenderTransformOrigin = new Point(0.5, 0.5),
        RenderTransform = new RotateTransform(rotate),
    };

    private static StackPanel LabeledGlyph(string glyph, string label)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(Glyph(glyph));
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(6, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    private ToggleButton AddToggle(Panel panel, UIElement content, string name, string shortcut, Action action)
    {
        var button = new ToggleButton { Content = content, ToolTip = $"{name} ({shortcut})", Focusable = false, Margin = ButtonSpacing, Style = (Style)FindResource("Captio.BarToggle") };
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => { action(); UpdateToolbarState(); };
        panel.Children.Add(button);
        return button;
    }

    private Button AddButton(Panel panel, UIElement content, string name, string? shortcut, Action action)
    {
        var button = new Button
        {
            Content = content,
            ToolTip = shortcut is null ? name : $"{name} ({shortcut})",
            Focusable = false,
            Margin = ButtonSpacing,
            Style = (Style)FindResource("Captio.BarButton"),
        };
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action();
        panel.Children.Add(button);
        return button;
    }

    private Border Separator() => _vertical
        ? new Border { Height = 1, Margin = new Thickness(5, 4, 5, 4), Background = Res("Captio.BarBorder") }
        : new Border { Width = 1, Margin = new Thickness(4, 5, 4, 5), Background = Res("Captio.BarBorder") };

    /// <summary>1 DIP on each side along the bar only, so the gap between buttons never widens the bar.</summary>
    private Thickness ButtonSpacing => _vertical ? new Thickness(0, 1, 0, 1) : new Thickness(1, 0, 1, 0);

    private Brush Res(string key) => (Brush)FindResource(key);

    private void UpdateToolbarState()
    {
        foreach (var (tool, button) in _toolButtons) button.IsChecked = tool == _tool;
        _shapeButton.Content = ShapeIcon(_shape);
        _shapeButton.ToolTip = $"Forma: {ShapeName(_shape)}. Clic para elegir (F usa la actual)";
        AutomationProperties.SetName(_shapeButton, ShapeName(_shape));
        _colorSwatch.Fill = new SolidColorBrush(AnnotationRenderer.ToColor(_color));
        var colorName = ShapePalette.Find(_color)?.Name ?? "Color";
        if (((FrameworkElement)_colorSwatch.Parent).Parent is Button colorButton)
        {
            AutomationProperties.SetName(colorButton, $"Color: {colorName}");
            colorButton.ToolTip = $"Color: {colorName} (C)";
        }
        foreach (var (kind, item) in _shapeItems) item.FontWeight = kind == _shape ? FontWeights.SemiBold : FontWeights.Normal;
        if (_openPopup is null) PaintPopupItems();
        _undoButton.IsEnabled = Document.CanUndo;
        _redoButton.IsEnabled = Document.CanRedo;
        _deleteButton.IsEnabled = _selected >= 0;
    }

    private void PositionToolbar()
    {
        if (_region is not { } r) return;
        _toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _toolbar.DesiredSize;
        var position = ToolbarPlacement.PlaceOnMonitor(
            new Bounds(r.X, r.Y, r.Width, r.Height), size.Width, size.Height,
            new Bounds(0, 0, Snapshot.Bounds.Width, Snapshot.Bounds.Height), _side, Scale);
        Canvas.SetLeft(_toolbar, position.X);
        Canvas.SetTop(_toolbar, position.Y);
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
            if (owner._preview is { } preview && owner._drag is Drag.DrawShape or Drag.DrawRedaction) AnnotationRenderer.Draw(dc, preview);
            dc.Pop();

            dc.DrawGeometry(Dim, null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(r)));

            var pen = RegionPen.Clone();
            pen.Thickness = 1.5 * s;
            dc.DrawRectangle(null, pen, r);

            if (owner._selected >= 0 && owner._selected < items.Count && owner._drag != Drag.MoveAnnotation)
            {
                var b = ShapeFactory.BoundsOf(items[owner._selected]);
                var bound = new Rect(b.X, b.Y, b.Width, b.Height);
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
    }
}
