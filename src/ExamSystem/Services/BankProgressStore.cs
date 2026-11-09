using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamSystem.Models;

namespace ExamSystem.Services;

/// <summary>
/// 一库一个进度文件的实现：progress/{bankId}.json。
///
/// 读失败一律返回空进度——进度丢了最多重练一遍，不该让练习页打不开。
/// 写失败交给调用方提示，因为那才是用户需要知道的。
/// </summary>
public sealed class BankProgressStore(string directory) : IBankProgressStore
{
    /// <summary>默认使用用户数据目录。</summary>
    public BankProgressStore()
        : this(AppPaths.ProgressDirectory)
    {
    }

    public async Task<BankProgress> LoadAsync(string bankId, CancellationToken ct = default)
    {
        if (!AppPaths.IsValidBankId(bankId))
        {
            return new BankProgress();
        }

        var path = PathFor(bankId);
        if (File.Exists(path))
        {
            try
            {
                var text = await File.ReadAllTextAsync(path, Encoding.UTF8, ct).ConfigureAwait(false);
                var progress = JsonSerializer.Deserialize(text, JsonContext.BankProgressInfo);
                if (progress is not null)
                {
                    return progress;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // 进度文件坏了：当作没有进度，下一次保存会覆盖它。
            }
        }

        return new BankProgress();
    }

    public async Task SaveAsync(string bankId, BankProgress progress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(progress);

        if (!AppPaths.IsValidBankId(bankId))
        {
            throw new ArgumentException($"非法的题库 Id：{bankId}", nameof(bankId));
        }

        if (progress.IsEmpty)
        {
            // 没有进度就不留空文件，下次读取走「没有进度」的分支。
            await DeleteAsync(bankId, ct).ConfigureAwait(false);
            return;
        }

        Directory.CreateDirectory(directory);
        progress.SchemaVersion = BankProgress.CurrentSchemaVersion;
        progress.UpdatedAt = DateTimeOffset.Now;

        var json = JsonSerializer.Serialize(progress, JsonContext.BankProgressInfo);
        await AtomicFile.WriteAllTextAsync(PathFor(bankId), json, ct).ConfigureAwait(false);
    }

    public Task DeleteAsync(string bankId, CancellationToken ct = default)
    {
        if (AppPaths.IsValidBankId(bankId))
        {
            TryDelete(PathFor(bankId));
        }

        return Task.CompletedTask;
    }

    private string PathFor(string bankId) => Path.Combine(directory, bankId + ".json");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            var temp = path + ".tmp";
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 删不掉不影响使用，最多留个孤儿文件。
        }
    }
}
