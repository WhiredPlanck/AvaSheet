using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ExamSystem.Models;

namespace ExamSystem.Services;

/// <summary>
/// 一库一 JSON 的实现。
///
/// 三条硬约束：
/// 1. 写入先落到 *.json.tmp，Flush 之后 File.Move 覆盖，进程被杀也不会留下半个文件；
/// 2. 所有 IO 都是异步的，且都接 CancellationToken；
/// 3. ListAsync 靠「文件长度 + 修改时间」缓存摘要，题库多时不必每次全量解析。
/// </summary>
public sealed class BankStore : IBankStore
{
    private const string TempSuffix = ".tmp";

    private readonly string _directory;

    /// <summary>文件名 → (文件签名, 摘要)。文件一改签名就变，缓存自然失效。</summary>
    private readonly Dictionary<string, (string Signature, BankSummary Summary)> _summaryCache =
        new(StringComparer.Ordinal);

    private readonly List<string> _unreadableFiles = [];

    /// <summary>写操作串行化，避免同一库并发写互相踩。</summary>
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public BankStore(string directory)
    {
        _directory = directory;
    }

    /// <summary>默认使用用户数据目录。</summary>
    public BankStore()
        : this(AppPaths.BanksDirectory)
    {
    }

    public IReadOnlyList<string> UnreadableFiles => _unreadableFiles;

    public async Task<IReadOnlyList<BankSummary>> ListAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_directory);

        var result = new List<BankSummary>();
        var alive = new HashSet<string>(StringComparer.Ordinal);
        _unreadableFiles.Clear();

        foreach (var path in Directory.EnumerateFiles(_directory, "*" + TempSuffix))
        {
            // 上次写到一半遗留的临时文件，直接清掉。
            TryDelete(path);
        }

        foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
        {
            ct.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(path);
            var id = Path.GetFileNameWithoutExtension(path);
            if (!AppPaths.IsValidBankId(id))
            {
                _unreadableFiles.Add(fileName);
                continue;
            }

            alive.Add(fileName);

            var file = new FileInfo(path);
            var signature = Signature(file);
            if (_summaryCache.TryGetValue(fileName, out var cached) && cached.Signature == signature)
            {
                result.Add(cached.Summary);
                continue;
            }

            try
            {
                var text = await File.ReadAllTextAsync(path, Encoding.UTF8, ct).ConfigureAwait(false);
                var bank = JsonSerializer.Deserialize(text, JsonContext.QuestionBankInfo);
                if (bank is null)
                {
                    _unreadableFiles.Add(fileName);
                    continue;
                }

                var summary = Summarize(id, bank);
                _summaryCache[fileName] = (signature, summary);
                result.Add(summary);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                _unreadableFiles.Add(fileName);
            }
        }

        // 已删除的文件清出缓存。
        foreach (var stale in _summaryCache.Keys.Where(k => !alive.Contains(k)).ToList())
        {
            _summaryCache.Remove(stale);
        }

        return result
            .OrderByDescending(s => s.UpdatedAt)
            .ThenBy(s => s.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    public async Task<QuestionBank?> LoadAsync(string id, CancellationToken ct = default)
    {
        var path = PathFor(id);
        if (!File.Exists(path))
        {
            return null;
        }

        var text = await File.ReadAllTextAsync(path, Encoding.UTF8, ct).ConfigureAwait(false);
        var bank = JsonSerializer.Deserialize(text, JsonContext.QuestionBankInfo)
                   ?? throw new InvalidDataException($"题库文件为空：{Path.GetFileName(path)}");

        if (bank.SchemaVersion > QuestionBank.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"题库版本 {bank.SchemaVersion} 高于当前程序支持的 {QuestionBank.CurrentSchemaVersion}，请升级程序。");
        }

        bank.Id = id;
        bank.Questions ??= [];
        bank.WrongQuestionIds ??= [];
        bank.PracticeProgress ??= [];
        return bank;
    }

    public async Task<string> CreateAsync(string name, string? description = null, CancellationToken ct = default)
    {
        var trimmed = ValidateName(name);
        var now = DateTimeOffset.Now;
        var bank = new QuestionBank
        {
            Id = NewId(),
            Name = trimmed,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            Questions = [],
        };

        await SaveAsync(bank, ct).ConfigureAwait(false);
        return bank.Id;
    }

    public async Task RenameAsync(string id, string newName, CancellationToken ct = default)
    {
        var trimmed = ValidateName(newName);
        var path = PathFor(id);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("题库不存在。", path);
        }

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 只改 name / updatedAt：用 JsonNode 定点改，不反序列化题目。
            var text = await File.ReadAllTextAsync(path, Encoding.UTF8, ct).ConfigureAwait(false);
            var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) as JsonObject ?? throw new InvalidDataException("题库文件格式不正确。");

            SetProperty(node, "name", trimmed);
            SetProperty(node, "updatedAt", DateTimeOffset.Now.ToString("O"));

            await WriteAtomicAsync(path, node.ToJsonString(JsonContext.Io), ct).ConfigureAwait(false);
            _summaryCache.Remove(Path.GetFileName(path));
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var path = PathFor(id);

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            TryDelete(path + TempSuffix);
            _summaryCache.Remove(Path.GetFileName(path));
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task SaveAsync(QuestionBank bank, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bank);
        bank.Name = ValidateName(bank.Name);
        bank.Questions ??= [];
        bank.WrongQuestionIds ??= [];
        bank.PracticeProgress ??= [];
        bank.SchemaVersion = QuestionBank.CurrentSchemaVersion;
        bank.UpdatedAt = DateTimeOffset.Now;
        if (bank.CreatedAt == default)
        {
            bank.CreatedAt = bank.UpdatedAt;
        }

        var path = PathFor(bank.Id);

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);
            var json = JsonSerializer.Serialize(bank, JsonContext.QuestionBankInfo);
            await WriteAtomicAsync(path, json, ct).ConfigureAwait(false);
            _summaryCache.Remove(Path.GetFileName(path));
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public Task<bool> ExistsAsync(string id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(AppPaths.IsValidBankId(id) && File.Exists(PathFor(id)));
    }

    // ---------------------------------------------------------------- 内部

    private static BankSummary Summarize(string id, QuestionBank bank) => new(
        id,
        string.IsNullOrWhiteSpace(bank.Name) ? id : bank.Name,
        bank.Description,
        bank.Questions?.Count ?? 0,
        bank.UpdatedAt,
        bank.WrongQuestionIds?.Count(w => bank.Questions?.Any(q => q.Id == w) == true) ?? 0);

    private static string Signature(FileInfo file) => $"{file.Length}:{file.LastWriteTimeUtc.Ticks}";

    private static string NewId() => "bank_" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>本实例目录下的题库文件路径。Id 先校验，杜绝路径穿越。</summary>
    private string PathFor(string id)
    {
        if (!AppPaths.IsValidBankId(id))
        {
            throw new ArgumentException($"非法的题库 Id：{id}", nameof(id));
        }

        return Path.Combine(_directory, id + ".json");
    }

    private static string ValidateName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("题库名称不能为空。", nameof(name));
        }

        return trimmed.Length > 60 ? trimmed[..60] : trimmed;
    }

    /// <summary>先写 .tmp 再原子替换，避免半截文件。</summary>
    private static async Task WriteAtomicAsync(string path, string content, CancellationToken ct)
    {
        var tmp = path + TempSuffix;
        var bytes = Encoding.UTF8.GetBytes(content);

        await using (var stream = new FileStream(
                         tmp,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 64 * 1024,
                         FileOptions.Asynchronous))
        {
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        File.Move(tmp, path, overwrite: true);
    }

    private static void SetProperty(JsonObject node, string name, string value)
    {
        var existing = node
            .Select(p => p.Key)
            .FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));

        node[existing ?? name] = value;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清理失败不影响主流程。
        }
    }
}
