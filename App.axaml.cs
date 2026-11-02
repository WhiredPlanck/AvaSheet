using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Fonts;
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

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = new BankStore();
            var settingsStore = new AppSettingsStore();
            var window = new MainWindow();

            window.DataContext = new MainViewModel(store, settingsStore, new DialogService(window));
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
}