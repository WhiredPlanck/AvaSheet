using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamSystem.Models;

namespace ExamSystem.Services;

/// <summary>settings.json 的读写。读写失败不影响主流程，一律吞掉并回落到默认值。</summary>
public sealed class AppSettingsStore(string path)
{
    public AppSettingsStore()
        : this(AppPaths.SettingsFile)
    {
    }

    public AppSettings Current { get; private set; } = new();

    /// <summary>
    /// 启动早期同步读一次设置。渲染模式必须在 Avalonia 初始化之前定下来，
    /// 这里不能等异步加载；文件缺失或损坏一律回落默认值（硬件加速关闭）。
    /// </summary>
    public static AppSettings LoadForStartup(string? path = null)
    {
        try
        {
            var file = path ?? AppPaths.SettingsFile;
            if (!File.Exists(file))
            {
                return new AppSettings();
            }

            var text = File.ReadAllText(file, Encoding.UTF8);
            return JsonSerializer.Deserialize(text, JsonContext.AppSettingsInfo) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return new AppSettings();
        }
    }

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(path))
            {
                Current = new AppSettings();
                return Current;
            }

            var text = await File.ReadAllTextAsync(path, Encoding.UTF8, ct).ConfigureAwait(false);
            Current = JsonSerializer.Deserialize(text, JsonContext.AppSettingsInfo) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Current = new AppSettings();
        }

        return Current;
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(Current, JsonContext.AppSettingsInfo);
            await AtomicFile.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 偏好设置丢了不影响使用。
        }
    }
}
