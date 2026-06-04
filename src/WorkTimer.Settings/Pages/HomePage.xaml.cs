using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using WorkTimer.Core.IPC;
using WorkTimer.Core.Settings;

namespace WorkTimer_Settings.Pages;

public sealed partial class HomePage : Page
{
    public AppSettings Settings { get; }

    private readonly SettingsManager _mgr = new();
    private bool _suppressIpc;

    public HomePage()
    {
        InitializeComponent();
        Settings = _mgr.Data;

        // 初始值
        HoverSlider.Value = Settings.HoverDelaySeconds;
        OpacitySlider.Value = Settings.DefaultOpacity;

        // 拖动滑块时实时同步到悬浮窗
        HoverSlider.ValueChanged += OnHoverChanged;
        OpacitySlider.ValueChanged += OnOpacityChanged;
    }

    private async void OnHoverChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressIpc) return;
        Settings.HoverDelaySeconds = e.NewValue;
        await SettingsIpc.NotifySetAsync("HoverDelaySeconds", e.NewValue.ToString("F1"));
    }

    private async void OnOpacityChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressIpc) return;
        Settings.DefaultOpacity = e.NewValue;
        await SettingsIpc.NotifySetAsync("DefaultOpacity", e.NewValue.ToString("G"));
    }

    private async void ReloadBtn_Click(object sender, RoutedEventArgs e)
    {
        await SettingsIpc.NotifyReloadAsync();

        // 从文件重载并刷新界面
        _suppressIpc = true;
        _mgr.Load();
        _suppressIpc = false;

        var dialog = new ContentDialog
        {
            Title = "提示",
            Content = "已通知悬浮窗从配置文件重载全部设置。",
            CloseButtonText = "确定",
            XamlRoot = XamlRoot
        };
        _ = dialog.ShowAsync();
    }
}
