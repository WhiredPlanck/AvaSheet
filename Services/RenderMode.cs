using System.Collections.Generic;
using Avalonia;

namespace ExamSystem.Services;

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
        => hardwareAcceleration
            ? [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software]
            : [Win32RenderingMode.Software];

    /// <summary>Linux：同上，默认先 GLX 再软件。</summary>
    public static IReadOnlyList<X11RenderingMode> X11Modes(bool hardwareAcceleration)
        => hardwareAcceleration
            ? [X11RenderingMode.Glx, X11RenderingMode.Software]
            : [X11RenderingMode.Software];

    /// <summary>macOS：同上，默认先 Metal 再 OpenGL 再软件。</summary>
    public static IReadOnlyList<AvaloniaNativeRenderingMode> NativeModes(bool hardwareAcceleration)
        => hardwareAcceleration
            ? [
                AvaloniaNativeRenderingMode.Metal,
                AvaloniaNativeRenderingMode.OpenGl,
                AvaloniaNativeRenderingMode.Software,
            ]
            : [AvaloniaNativeRenderingMode.Software];

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
