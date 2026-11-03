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
/// 题库文件的导入导出。全部构建在 <see cref="IBankStore"/> 之上：
/// 导出 = LoadAsync + 写目标流；导入 = 校验 + 换新 Id + 一次 SaveAsync。
/// </summary>
public sealed class BankTransfer
{
    private readonly IBankStore _store;

    public BankTransfer(IBankStore store)
    {
        _store = store;
    }

    /// <summary>导入一个题库文件，返回落库后的摘要。</summary>
    public async Task<BankSummary> ImportAsync(Stream stream, CancellationToken ct = default)
    {
        QuestionBank? bank;
        try
        {
            bank = await JsonSerializer
                .DeserializeAsync<QuestionBank>(stream, JsonContext.Io, ct)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("这不是有效的题库 JSON 文件，或文件格式与本程序不兼容。", ex);
        }

        if (bank is null)
        {
            throw new InvalidDataException("文件内容为空。");
        }

        if (bank.SchemaVersion > QuestionBank.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"题库版本 {bank.SchemaVersion} 高于当前程序支持的 {QuestionBank.CurrentSchemaVersion}，请升级程序后再导入。");
        }

        bank.Questions ??= [];
        NormalizeQuestions(bank);

        // 题目 Id 会被重新编号，别人库里的错题本和练习进度在这里没有意义。
        bank.WrongQuestionIds = [];
        bank.PracticeProgress = [];

        var existing = await _store.ListAsync(ct).ConfigureAwait(false);
        var name = string.IsNullOrWhiteSpace(bank.Name) ? "导入的题库" : bank.Name.Trim();
        if (existing.Any(b => string.Equals(b.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            name += "（导入）";
        }

        var now = DateTimeOffset.Now;
        bank.Id = "bank_" + Guid.NewGuid().ToString("N")[..12];
        bank.Name = name;
        bank.UpdatedAt = now;
        if (bank.CreatedAt == default)
        {
            bank.CreatedAt = now;
        }

        await _store.SaveAsync(bank, ct).ConfigureAwait(false);

        return new BankSummary(bank.Id, bank.Name, bank.Description, bank.Questions.Count, bank.UpdatedAt);
    }

    /// <summary>把一个题库写到目标流。</summary>
    public async Task ExportAsync(string id, Stream target, CancellationToken ct = default)
    {
        var bank = await _store.LoadAsync(id, ct).ConfigureAwait(false)
                   ?? throw new FileNotFoundException("题库不存在，可能已被删除。");

        await JsonSerializer.SerializeAsync(target, bank, JsonContext.Io, ct).ConfigureAwait(false);
        await target.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 补齐缺失的题目 Id、清理空选项，并把判断题的答案统一成 T / F 大写。
    /// 导入的是外部文件，不能假设字段齐全。
    /// </summary>
    private static void NormalizeQuestions(QuestionBank bank)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var question in bank.Questions)
        {
            if (question is null)
            {
                continue;
            }

            question.Choices ??= [];
            question.Answers ??= [];
            question.Tags ??= [];
            question.Stem ??= "";
            question.Choices.RemoveAll(c => c is null || string.IsNullOrWhiteSpace(c.Key));
            question.Answers = [.. question.Answers
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim())];
            question.Difficulty = Math.Clamp(question.Difficulty, 1, 5);

            if (string.IsNullOrWhiteSpace(question.Id) || !used.Add(question.Id))
            {
                question.Id = "q_" + Guid.NewGuid().ToString("N")[..10];
                used.Add(question.Id);
            }
        }
    }
}
