using System.Windows;
using System.Windows.Controls;

namespace Captio.LibraryUi;

internal sealed class InputDialog : Window
{
    private readonly TextBox _box;

    public InputDialog(string title, string label, string initial)
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _box = new TextBox { Text = initial, Margin = new Thickness(0, 6, 0, 0) };
        var ok = new Button { Content = "Aceptar", MinWidth = 96, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "Cancelar", MinWidth = 96, IsCancel = true };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = label });
        root.Children.Add(_box);
        root.Children.Add(buttons);
        Content = root;

        Loaded += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
        };
    }

    public string Value => _box.Text;
}
