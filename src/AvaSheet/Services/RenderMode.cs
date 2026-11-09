using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;

namespace AvaSheet.Services;

/// <summary>
/// 启动时决定用哪种渲染后端。
/// Avalonia 默认「硬件优先，失败回落软件」，但有些显卡能初始化成功、跑一阵才卡死，
/// 那种情况回退不了，所以这里默认只用软件渲染，由用户显式开启硬件加速。
/// 渲染模式只在启动时读一次，改设置必须重启程序。
/// </summary>
public static class RenderMode
{
    /// <summary>Windows：开启硬件加速时沿用 Avalonia 默认顺序，保留软件兜底。</summary>
    public static IReadOnlyList<Win32RenderingMode> Win32Modes(bool hardwareAcceleration)
        => Order(new Win32PlatformOptions().RenderingMode, Win32RenderingMode.Software, hardwareAcceleration);

    /// <summary>Linux：同上，默认先 GLX 再软件。</summary>
    public static IReadOnlyList<X11RenderingMode> X11Modes(bool hardwareAcceleration)
        => Order(new X11PlatformOptions().RenderingMode, X11RenderingMode.Software, hardwareAcceleration);

    /// <summary>macOS：同上，默认先 Metal 再 OpenGL 再软件。</summary>
    public static IReadOnlyList<AvaloniaNativeRenderingMode> NativeModes(bool hardwareAcceleration)
        => Order(new AvaloniaNativePlatformOptions().RenderingMode, AvaloniaNativeRenderingMode.Software, hardwareAcceleration);

    /// <summary>
    /// 关闭时只留软件渲染；开启时沿用平台默认的硬件后端顺序，再把软件追加到末尾兜底。
    /// 顺序从平台选项的默认值派生，Avalonia 调整默认列表时这里不会跟着漂移。
    /// </summary>
    private static IReadOnlyList<T> Order<T>(IReadOnlyList<T> defaults, T software, bool hardwareAcceleration)
        where T : struct, Enum
    {
        if (!hardwareAcceleration)
        {
            return [software];
        }

        var hardware = defaults.Where(mode => !EqualityComparer<T>.Default.Equals(mode, software));

        // 平台的默认列表里本来就有软件兜底，去掉后重新追加，保证它排在最后。
        return [.. hardware, software];
    }

    /// <summary>
    /// 把渲染模式塞进 AppBuilder。三个平台的选项都会被注册，
    /// 实际只有当前平台的实现会去读自己那份。
    /// </summary>
    public static AppBuilder Apply(AppBuilder builder, bool hardwareAcceleration)
        => builder
            .With(new Win32PlatformOptions { RenderingMode = Win32Modes(hardwareAcceleration) })
            .With(new X11PlatformOptions { RenderingMode = X11Modes(hardwareAcceleration) })
            .With(new AvaloniaNativePlatformOptions { RenderingMode = NativeModes(hardwareAcceleration) });
}
