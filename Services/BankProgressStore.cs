using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
public sealed class BankProgressStore : IBankProgressStore
{
    private readonly string _directory;
    private readonly string _banksDirectory;

    public BankProgressStore(string directory, string banksDirectory)
    {
        _directory = directory;
        _banksDirectory = banksDirectory;
    }

    /// <summary>默认使用用户数据目录。</summary>
    public BankProgressStore()
        : this(AppPaths.ProgressDirectory, AppPaths.BanksDirectory)
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
                    Normalize(progress);
                    return progress;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // 进度文件坏了：当作没有进度，下一次保存会覆盖它。
            }
        }

        return await MigrateFromBankAsync(bankId, ct).ConfigureAwait(false);
    }

    public async Task SaveAsync(string bankId, BankProgress progress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(progress);

        if (!AppPaths.IsValidBankId(bankId))
        {
            throw new ArgumentException($"非法的题库 Id：{bankId}", nameof(bankId));
        }

        Normalize(progress);

        if (progress.IsEmpty)
        {
            // 没有进度就不留空文件，下次读取走「没有进度」的分支。
            await DeleteAsync(bankId, ct).ConfigureAwait(false);
            return;
        }

        Directory.CreateDirectory(_directory);
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

    private string PathFor(string bankId) => Path.Combine(_directory, bankId + ".json");

    /// <summary>
    /// 旧版本把错题本和练习进度写在题库文件里。第一次读到某个库时把这两样搬出来，
    /// 题库文件本身等下一次保存时自然就不再带这些字段了。
    /// </summary>
    private async Task<BankProgress> MigrateFromBankAsync(string bankId, CancellationToken ct)
    {
        var progress = new BankProgress();
        var bankFile = Path.Combine(_banksDirectory, bankId + ".json");
        if (!File.Exists(bankFile))
        {
            return progress;
        }

        try
        {
            var text = await File.ReadAllTextAsync(bankFile, Encoding.UTF8, ct).ConfigureAwait(false);
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            var root = document.RootElement;

            if (TryGetProperty(root, "wrongQuestionIds", out var wrong)
                && wrong.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in wrong.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String
                        && item.GetString() is { Length: > 0 } id
                        && !progress.WrongQuestionIds.Contains(id, StringComparer.Ordinal))
                    {
                        progress.WrongQuestionIds.Add(id);
                    }
                }
            }

            if (TryGetProperty(root, "practiceProgress", out var entries)
                && entries.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in entries.EnumerateArray())
                {
                    if (!TryGetProperty(entry, "type", out var typeElement))
                    {
                        continue;
                    }

                    var typeText = typeElement.ValueKind == JsonValueKind.String ? typeElement.GetString() : null;
                    if (!QuestionTypeExtensions.TryParseLabel(typeText, out var type))
                    {
                        continue;
                    }

                    var done = new List<string>();
                    if (TryGetProperty(entry, "doneIds", out var doneIds)
                        && doneIds.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in doneIds.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String
                                && item.GetString() is { Length: > 0 } id
                                && !done.Contains(id, StringComparer.Ordinal))
                            {
                                done.Add(id);
                            }
                        }
                    }

                    if (done.Count > 0)
                    {
                        progress.PracticeProgress.Add(new PracticeProgressEntry
                        {
                            Type = type,
                            DoneIds = done,
                        });
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new BankProgress();
        }

        if (!progress.IsEmpty)
        {
            try
            {
                await SaveAsync(bankId, progress, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 迁移失败也不影响本次使用，下一次读取会再试。
            }
        }

        return progress;
    }

    /// <summary>旧文件可能是 PascalCase（早期版本写错过），属性名不区分大小写地找。</summary>
    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static void Normalize(BankProgress progress)
    {
        progress.WrongQuestionIds ??= [];
        progress.PracticeProgress ??= [];

        foreach (var entry in progress.PracticeProgress)
        {
            entry.DoneIds ??= [];
        }
    }

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
