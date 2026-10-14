using System.Windows;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace USBBlockManager.Views;

public partial class PasteLicenseWindow : Window
{
    public PasteLicenseWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => LicenseTextBox.Focus();
    }

    public string LicenseJson => LicenseTextBox.Text;

    private void PasteFromClipboard_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
                LicenseTextBox.Text = Clipboard.GetText();
        }
        catch
        {
            // Clipboard может быть недоступен
        }
    }

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LicenseTextBox.Text))
        {
            MessageBox.Show(
                "Вставьте текст лицензии или отмените.",
                "USB Block Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }
}