using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Logging;
using Avalonia.Media;

namespace ExamSystem;

sealed class Program
{
    // 中文回退链，顺序即字形质量优先级。
    // 目标机上缺哪个就静默跳过（字体名解析失败只是 continue），不会报错。
    internal static readonly string[] CjkFontCandidates =
    {
        "Noto Sans CJK SC",
        "Source Han Sans CN",
        "Source Han Sans SC",
        "Noto Sans SC",
        "WenQuanYi Zen Hei",
        "文泉驿正黑",
        "SimHei",
    };

    // 由 --font-diag 打开，App 启动后输出字体自检信息。
    internal static bool DumpFontDiagnostics { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        DumpFontDiagnostics = args.Contains("--font-diag", StringComparer.Ordinal);

        // Skia 会把 CurrentUICulture.Name 当作 BCP-47 传给 fontconfig（FC_LANG），
        // 决定 CJK 字形取简/繁/日/韩哪一套。信创机常见 LANG=C，这里锁成 zh-CN。
        var zh = CultureInfo.GetCultureInfo("zh-CN");
        CultureInfo.DefaultThreadCurrentCulture = zh;
        CultureInfo.DefaultThreadCurrentUICulture = zh;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
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
            })
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
