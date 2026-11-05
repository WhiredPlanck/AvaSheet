using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamSystem.Models;

namespace ExamSystem.Services;

/// <summary>
/// 考试记录存储：所有记录放在一个 records.json 里。
///
/// 记录只增不改，单文件足够；为了不让文件无限增长，只保留最近 <see cref="MaxRecords"/> 条，
/// 超出时丢掉交卷时间最早的。写文件走临时文件 + 原子替换，
/// 避免断电或崩溃留下半个 JSON。
/// </summary>
public sealed class ExamRecordStore : IExamRecordStore
{
    /// <summary>最多保留多少条记录。</summary>
    public const int MaxRecords = 200;

    private readonly string _file;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private List<ExamRecord>? _cache;

    public ExamRecordStore()
        : this(AppPaths.RecordsFile)
    {
    }

    public ExamRecordStore(string file)
    {
        _file = file;
    }

    public async Task<IReadOnlyList<ExamRecord>> ListAsync()
    {
        var records = await LoadAsync();
        return records.OrderByDescending(r => r.FinishedAt).ToList();
    }

    public async Task SaveAsync(ExamRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _gate.WaitAsync();
        try
        {
            var records = await LoadAsync();
            records.RemoveAll(r => r.Id == record.Id);
            records.Add(record);

            if (records.Count > MaxRecords)
            {
                foreach (var stale in records.OrderBy(r => r.FinishedAt).Take(records.Count - MaxRecords).ToList())
                {
                    records.Remove(stale);
                }
            }

            await WriteAsync(records);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string id)
    {
        await _gate.WaitAsync();
        try
        {
            var records = await LoadAsync();
            if (records.RemoveAll(r => r.Id == id) > 0)
            {
                await WriteAsync(records);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _cache = [];
            await WriteAsync(_cache);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---------------------------------------------------------------- 内部

    private async Task<List<ExamRecord>> LoadAsync()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        if (!File.Exists(_file))
        {
            return _cache = [];
        }

        try
        {
            await using var stream = new FileStream(
                _file, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);

            var records = await JsonSerializer.DeserializeAsync(stream, JsonContext.ExamRecordListInfo)
                          ?? [];

            foreach (var record in records)
            {
                record.Answers ??= [];
            }

            return _cache = records;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 文件坏了不能让考试成绩页直接崩掉：当作空历史，下一次写入会覆盖它。
            return _cache = [];
        }
    }

    private async Task WriteAsync(List<ExamRecord> records)
    {
        var directory = Path.GetDirectoryName(_file);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(records, JsonContext.ExamRecordListInfo);
        await AtomicFile.WriteAllTextAsync(_file, json).ConfigureAwait(false);
    }
}
