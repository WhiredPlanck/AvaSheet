using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Platform;
using ExamSystem.Services;
using ExamSystem.ViewModels;
using ExamSystem.Views;

namespace ExamSystem;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (Program.DumpFontDiagnostics)
        {
            WriteFontDiagnostics();
        }

        if (Program.DumpRenderDiagnostics)
        {
            WriteRenderDiagnostics();
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = new BankStore();
            var settingsStore = new AppSettingsStore();
            var recordStore = new ExamRecordStore();
            var window = new MainWindow();

            window.DataContext = new MainViewModel(store, settingsStore, new DialogService(window), recordStore);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 打印实际命中的中文字体，用于信创机现场排查。启动参数加 --font-diag 开启。
    /// </summary>
    private static void WriteFontDiagnostics()
    {
        var fontManager = FontManager.Current;
        Console.WriteLine($"[Font] DefaultFamily = {fontManager.DefaultFontFamily.Name}");
        Console.WriteLine($"[Font] InstalledFamilyCount = {fontManager.SystemFonts.Count}");

        foreach (var name in Program.CjkFontCandidates)
        {
            if (!fontManager.TryGetGlyphTypeface(new Typeface(new FontFamily(name)), out var glyphTypeface))
            {
                continue;
            }

            // 找不到族名时 TryGetGlyphTypeface 会退化到默认字体并返回 true，
            // 必须比对 FamilyName 才能确认这个字体真的存在。
            if (!string.Equals(glyphTypeface.FamilyName, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Console.WriteLine(
                $"[Font] OK  {name,-22} 中={glyphTypeface.SupportedUnicodeRange.IsInRange(0x4E2D)} " +
                $"𠮷={glyphTypeface.SupportedUnicodeRange.IsInRange(0x20BB7)} " +
                $"GB={glyphTypeface.CodePageCoverage.HasFlag(FontCodePageCoverage.ChineseSimplified)}");
        }
    }

    /// <summary>
    /// 打印本次启动实际使用的渲染后端，用于现场排查显卡问题。启动参数加 --render-diag 开启。
    /// 判定依据：平台在软件渲染模式下不注册 IPlatformGraphics，硬件后端则注册一个实例。
    /// Avalonia 12 把 AvaloniaLocator 归为私有 API，编译期不可见，只能反射读取；
    /// DynamicDependency 保证 AOT 裁剪后这两个成员仍在。
    /// </summary>
    private static void WriteRenderDiagnostics()
    {
        Console.WriteLine($"[Render] Requested = {(Program.HardwareAccelerationEnabled ? "hardware" : "software")}");
        Console.WriteLine($"[Render] RequestedSource = {(Program.RenderModeFromCommandLine ? "command-line" : "settings")}");

        if (!TryGetPlatformGraphics(out var graphics))
        {
            // 反射失败不能说成软件渲染，否则升级 Avalonia 后排查会被误导。
            Console.WriteLine("[Render] Actual = unknown (reflection failed)");
            return;
        }

        Console.WriteLine(graphics is null
            ? "[Render] Actual = software"
            : $"[Render] Actual = hardware ({graphics.GetType().Name})");
    }

    /// <summary>
    /// 读取当前注册的 IPlatformGraphics。返回 false 表示反射本身失败（不是「没有注册」）。
    /// </summary>
    [DynamicDependency("get_CurrentMutable", typeof(AvaloniaLocator))]
    [DynamicDependency("GetService", typeof(AvaloniaLocator))]
    private static bool TryGetPlatformGraphics(out object? graphics)
    {
        graphics = null;

        try
        {
            var locatorType = typeof(AvaloniaLocator);
            var resolver = locatorType
                .GetProperty("CurrentMutable", BindingFlags.Public | BindingFlags.Static)?
                .GetValue(null);
            var getService = locatorType
                .GetMethod("GetService", BindingFlags.Public | BindingFlags.Instance, null, [typeof(Type)], null);

            if (resolver is null || getService is null)
            {
                return false;
            }

            graphics = getService.Invoke(resolver, [typeof(IPlatformGraphics)]);
            return true;
        }
        catch (Exception ex) when (ex is TargetInvocationException or MissingMemberException or AmbiguousMatchException)
        {
            return false;
        }
    }
}