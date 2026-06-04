namespace WorkTimer.Overlay;

public enum OverlayWindowState
{
    /// <summary>隐藏到托盘</summary>
    Hidden,
    /// <summary>默认穿透态，鼠标可穿透</summary>
    Passthrough,
    /// <summary>鼠标悬停 1s 后的交互态</summary>
    Interactive,
    /// <summary>左键托盘触发的查找模式</summary>
    FindMe,
    /// <summary>开机续接提示</summary>
    ContinuationPrompt,
}
