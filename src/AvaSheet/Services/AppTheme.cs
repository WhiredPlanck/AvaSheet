using Avalonia;
using Avalonia.Styling;
using AvaSheet.Models;

namespace AvaSheet.Services;

/// <summary>
/// 主题设置的应用入口。明暗切换交给 Avalonia 的 ThemeVariant，
/// 颜色令牌仍然是 <c>Styles/Tokens.axaml</c> 里的两套主题字典。
/// </summary>
public static class AppTheme
{
    /// <summary>按设置切换明暗主题。跟随系统时用 Default，由 Avalonia 自己跟随平台。</summary>
    public static void Apply(ThemeMode mode)
    {
        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = mode switch
            {
                ThemeMode.Light => ThemeVariant.Light,
                ThemeMode.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }
}
