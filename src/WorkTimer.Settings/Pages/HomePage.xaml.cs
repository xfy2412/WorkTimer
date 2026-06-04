using Microsoft.UI.Xaml.Controls;
using WorkTimer.Core.Settings;

namespace WorkTimer_Settings.Pages;

public sealed partial class HomePage : Page
{
    public AppSettings Settings { get; }

    public HomePage()
    {
        InitializeComponent();
        Settings = new SettingsManager().Data;
    }
}
