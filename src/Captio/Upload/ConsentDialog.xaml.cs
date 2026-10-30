using System.Windows;

namespace Captio.Upload;

public partial class ConsentDialog : Window
{
    /// <param name="readOnly">Shows the notice for reference only, from Settings.</param>
    public ConsentDialog(bool readOnly)
    {
        InitializeComponent();
        if (readOnly)
        {
            AcceptButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = "Cerrar";
        }
    }

    private void OnAccept(object sender, RoutedEventArgs e) => DialogResult = true;
}
