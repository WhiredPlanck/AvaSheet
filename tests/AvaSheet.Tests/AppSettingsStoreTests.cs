using Avalonia;
using AvaSheet.Models;
using AvaSheet.Services;
using Xunit;

namespace AvaSheet.Tests;

public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void LoadForStartup_MissingFileUsesDefaults()
    {
        var settings = AppSettingsStore.LoadForStartup(_temp.Combine("settings.json"));

        Assert.False(settings.HardwareAcceleration);
        Assert.Equal(ThemeMode.System, settings.Theme);
    }

    [Fact]
    public void LoadForStartup_CorruptFileUsesDefaults()
    {
        var path = _temp.Combine("settings.json");
        File.WriteAllText(path, "{ 坏了");

        var settings = AppSettingsStore.LoadForStartup(path);

        Assert.False(settings.HardwareAcceleration);
        Assert.Equal(ThemeMode.System, settings.Theme);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsPreferences()
    {
        var path = _temp.Combine("settings.json");
        var store = new AppSettingsStore(path);
        store.Current.HardwareAcceleration = true;
        store.Current.Theme = ThemeMode.Dark;
        store.Current.LastBankId = "bank_a";

        await store.SaveAsync();

        Assert.True(AppSettingsStore.LoadForStartup(path).HardwareAcceleration);
        var loaded = await new AppSettingsStore(path).LoadAsync();
        Assert.Equal(ThemeMode.Dark, loaded.Theme);
        Assert.Equal("bank_a", loaded.LastBankId);
    }

    [Fact]
    public async Task LoadAsync_CorruptFileFallsBackToDefaults()
    {
        var path = _temp.Combine("settings.json");
        File.WriteAllText(path, "{ 坏了");

        var settings = await new AppSettingsStore(path).LoadAsync();

        Assert.False(settings.HardwareAcceleration);
        Assert.Equal(ThemeMode.System, settings.Theme);
    }
}

public sealed class RenderModeTests
{
    [Fact]
    public void WithoutHardwareAcceleration_OnlySoftwareIsOffered()
    {
        Assert.Equal(
            new[] { Win32RenderingMode.Software },
            RenderMode.Win32Modes(hardwareAcceleration: false));
        Assert.Equal(
            new[] { X11RenderingMode.Software },
            RenderMode.X11Modes(hardwareAcceleration: false));
        Assert.Equal(
            new[] { AvaloniaNativeRenderingMode.Software },
            RenderMode.NativeModes(hardwareAcceleration: false));
    }

    [Fact]
    public void WithHardwareAcceleration_TriesGpuFirstAndKeepsSoftwareFallback()
    {
        var win32 = RenderMode.Win32Modes(hardwareAcceleration: true);
        var x11 = RenderMode.X11Modes(hardwareAcceleration: true);
        var native = RenderMode.NativeModes(hardwareAcceleration: true);

        Assert.Equal(Win32RenderingMode.AngleEgl, win32[0]);
        Assert.Equal(Win32RenderingMode.Software, win32[^1]);
        Assert.Equal(X11RenderingMode.Glx, x11[0]);
        Assert.Equal(X11RenderingMode.Software, x11[^1]);
        Assert.Equal(AvaloniaNativeRenderingMode.Metal, native[0]);
        Assert.Equal(AvaloniaNativeRenderingMode.Software, native[^1]);
    }
}

public sealed class AnswerTextTests
{
    [Fact]
    public void Join_TranslatesTrueFalseAndKeepsOptionKeys()
    {
        Assert.Equal("正确、错误", AnswerText.Join(["T", "F"]));
        Assert.Equal("A、B", AnswerText.Join(["A", "B"]));
        Assert.Equal("正确", AnswerText.Key("T"));
        Assert.Equal("错误", AnswerText.Key("F"));
        Assert.Equal("C", AnswerText.Key("C"));
    }
}
