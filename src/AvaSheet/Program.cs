using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Logging;
using Avalonia.Media;
using AvaSheet.Services;

namespace AvaSheet;

sealed class Program
{
    internal static readonly string[] CjkFontCandidates =
    [
        "Noto Sans CJK SC",
        "Noto Sans SC",
        "Source Han Sans CN",
        "Source Han Sans SC",
        "WenQuanYi Zen Hei",
        "文泉驿正黑",
        "DejaVu Sans",
        "SimHei"
    ];

    // 由 --font-diag 打开，App 启动后输出字体自检信息。
    internal static bool DumpFontDiagnostics { get; private set; }

    // 由 --render-diag 打开，App 启动后输出实际使用的渲染后端。
    internal static bool DumpRenderDiagnostics { get; private set; }

    /// <summary>
    /// 本次启动实际使用的渲染方式。默认软件渲染；
    /// 由 settings.json 的 HardwareAcceleration 决定，可用 --gpu / --software 临时覆盖。
    /// </summary>
    internal static bool HardwareAccelerationEnabled { get; private set; }

    /// <summary>本次启动的渲染方式是否来自命令行（而不是设置里的值）。</summary>
    internal static bool RenderModeFromCommandLine { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        DumpFontDiagnostics = args.Contains("--font-diag", StringComparer.Ordinal);
        DumpRenderDiagnostics = args.Contains("--render-diag", StringComparer.Ordinal);

        // --gpu / --software 只影响这一次运行，方便现场排查；不改设置文件。
        var forceHardware = args.Contains("--gpu", StringComparer.Ordinal);
        var forceSoftware = args.Contains("--software", StringComparer.Ordinal);
        RenderModeFromCommandLine = forceHardware || forceSoftware;
        HardwareAccelerationEnabled = forceHardware
            || (!forceSoftware && AppSettingsStore.LoadForStartup().HardwareAcceleration);

        // Skia 会把 CurrentUICulture.Name 当作 BCP-47 传给 fontconfig（FC_LANG），
        // 决定 CJK 字形取简/繁/日/韩哪一套。信创机常见 LANG=C，这里锁成 zh-CN。
        var zh = CultureInfo.GetCultureInfo("zh-CN");
        CultureInfo.DefaultThreadCurrentCulture = zh;
        CultureInfo.DefaultThreadCurrentUICulture = zh;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => RenderMode.Apply(
                AppBuilder.Configure<App>()
                    .UsePlatformDetect()
#if DEBUG
                    .WithDeveloperTools()
#endif
                    .WithInterFont()
                    .With(new FontManagerOptions
                    {
                        // 拉丁字形仍用内嵌的 Inter；其余字符走下面的回退链。
                        // DefaultFamilyName 故意不设：它是单值且不做存在性校验，
                        // 指向机器上不存在的字体时会让 Typeface 直接抛异常而不是回退。
                        FontFallbacks = BuildCjkFontFallbacks(),
                    }),
                HardwareAccelerationEnabled)
            .LogToTrace(LogEventLevel.Warning);

    private static List<FontFallback> BuildCjkFontFallbacks()
    {
        var fallbacks = new List<FontFallback>(CjkFontCandidates.Length);
        fallbacks.AddRange(CjkFontCandidates.Select(name => new FontFallback
        {
            FontFamily = new FontFamily(name),
            // UnicodeRange 保持默认的 0-10FFFD（全范围）。
            // 限制过窄会漏掉 ℃ ㎡ ① 这类只有中文字体才有的符号；
            // 是否真的用这个字体，由它是否含有对应字形决定。
        }));

        return fallbacks;
    }
}
