using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WorkTimer.Core;

namespace WorkTimer_Settings.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        AutoStartToggle.IsOn = AutoStartService.IsEnabled();
        AutoStartToggle.Toggled += (_, _) =>
        {
            if (AutoStartToggle.IsOn) AutoStartService.Enable();
            else AutoStartService.Disable();
        };
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkTimer");
        System.Diagnostics.Process.Start("explorer.exe", path);
    }
}
