using System.Windows;

namespace WorkTimer.Overlay;

public partial class DarkDialog : Window
{
    public DarkDialog()
    {
        InitializeComponent();
    }

    public DarkDialog(string message, string title = "提示")
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
