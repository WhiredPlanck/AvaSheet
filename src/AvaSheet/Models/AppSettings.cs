namespace AvaSheet.Models;

/// <summary>跨题库的用户偏好，存在 settings.json。</summary>
public sealed class AppSettings
{
    /// <summary>上次选中的题库 Id。</summary>
    public string? LastBankId { get; set; }

    /// <summary>主窗口是否最大化。</summary>
    public bool IsWindowMaximized { get; set; }

    /// <summary>
    /// 是否启用硬件加速。默认 false，用软件渲染：有的显卡能初始化成功但跑一阵会卡死，
    /// 那种情况 Avalonia 回退不了。改动在下次启动时生效。
    /// </summary>
    public bool HardwareAcceleration { get; set; }

    /// <summary>界面深浅主题，默认跟随系统。</summary>
    public ThemeMode Theme { get; set; } = ThemeMode.System;
}
