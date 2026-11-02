using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamSystem.Models;

namespace ExamSystem.Services;

/// <summary>settings.json 的读写。读写失败不影响主流程，一律吞掉并回落到默认值。</summary>
public sealed class AppSettingsStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _path;

    public AppSettingsStore(string path)
    {
        _path = path;
    }

    public AppSettingsStore()
        : this(AppPaths.SettingsFile)
    {
    }

    public AppSettings Current { get; private set; } = new();

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(_path))
            {
                Current = new AppSettings();
                return Current;
            }

            var text = await File.ReadAllTextAsync(_path, Encoding.UTF8, ct).ConfigureAwait(false);
            Current = JsonSerializer.Deserialize<AppSettings>(text, JsonContext.Io) ?? new AppSettings();
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
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            var json = JsonSerializer.Serialize(Current, JsonContext.Io);
            await File.WriteAllTextAsync(tmp, json, Utf8NoBom, ct).ConfigureAwait(false);
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 偏好设置丢了不影响使用。
        }
    }
}
