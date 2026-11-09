using System.Text;
using AvaSheet.Models;
using AvaSheet.Services;
using Xunit;

namespace AvaSheet.Tests;

public sealed class BankTransferTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly BankStore _store;
    private readonly BankTransfer _transfer;

    public BankTransferTests()
    {
        _store = new BankStore(_temp.Combine("banks"));
        _transfer = new BankTransfer(_store);
    }

    public void Dispose() => _temp.Dispose();

    private static MemoryStream Json(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task ImportAsync_RejectsBrokenJson()
    {
        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => _transfer.ImportAsync(Json("{ 不是题库")));

        Assert.Contains("不是有效的题库 JSON", error.Message);
    }

    [Fact]
    public async Task ImportAsync_RejectsEmptyContent()
    {
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => _transfer.ImportAsync(Json("null")));

        Assert.Equal("文件内容为空。", error.Message);
    }

    [Fact]
    public async Task ImportAsync_RejectsBankFromNewerVersion()
    {
        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => _transfer.ImportAsync(Json("""{"schemaVersion":99,"name":"新版本","questions":[]}""")));

        Assert.Contains("高于当前程序支持", error.Message);
    }

    [Fact]
    public async Task ImportAsync_AssignsNewIdAndKeepsQuestions()
    {
        await _store.SaveAsync(TestData.Bank(TestData.Single("q1")));

        var summary = await _transfer.ImportAsync(Json(
            """{"schemaVersion":4,"id":"bank_original","name":"测试题库","questions":[{"type":"SingleChoice","stem":"导入的题","choices":[{"key":"A","text":"甲"},{"key":"B","text":"乙"}],"answers":["A"]}]}"""));

        Assert.NotEqual("bank_original", summary.Id);
        Assert.Equal("测试题库（导入）", summary.Name);
        Assert.Equal(1, summary.QuestionCount);

        var imported = await _store.LoadAsync(summary.Id);
        Assert.NotNull(imported);
        Assert.Equal("导入的题", imported.Questions[0].Stem);
    }

    [Fact]
    public async Task ImportAsync_NormalizesBrokenQuestions()
    {
        var summary = await _transfer.ImportAsync(Json(
            """
            {
              "schemaVersion": 4,
              "name": "待整理",
              "questions": [
                {
                  "type": "SingleChoice",
                  "choices": [{ "key": "", "text": "空标识" }, { "key": "A", "text": "甲" }, { "key": "B", "text": "乙" }],
                  "answers": [" a "],
                  "difficulty": 9
                },
                {
                  "id": "q_dup",
                  "type": "TrueFalse",
                  "stem": "判断题",
                  "answers": ["T"],
                  "difficulty": 0
                },
                {
                  "id": "q_dup",
                  "type": "TrueFalse",
                  "stem": "重复 Id 的判断题",
                  "answers": ["F"]
                }
              ]
            }
            """));

        var bank = await _store.LoadAsync(summary.Id);
        Assert.NotNull(bank);

        Assert.Equal(3, bank.Questions.Count);
        Assert.All(bank.Questions, q => Assert.False(string.IsNullOrWhiteSpace(q.Id)));
        Assert.Equal(3, bank.Questions.Select(q => q.Id).Distinct().Count());
        Assert.StartsWith("q_", bank.Questions[0].Id);
        Assert.DoesNotContain(bank.Questions[0].Choices, c => c.Key.Length == 0);
        Assert.Equal(new[] { "a" }, bank.Questions[0].Answers);
        Assert.Equal(5, bank.Questions[0].Difficulty);
        Assert.Equal(1, bank.Questions[1].Difficulty);
    }

    [Fact]
    public async Task ExportAsync_WritesBankWithQuestions()
    {
        var bank = TestData.Bank(TestData.Single("q1"));
        await _store.SaveAsync(bank);

        using var target = new MemoryStream();
        await _transfer.ExportAsync(bank.Id, target);
        var text = Encoding.UTF8.GetString(target.ToArray());

        Assert.Contains("\"name\": \"测试题库\"", text);
        Assert.Contains("单选题", text);
        Assert.Contains("\"answers\"", text);
    }

    [Fact]
    public async Task ExportAsync_RejectsMissingBank()
    {
        var error = await Assert.ThrowsAsync<FileNotFoundException>(
            () => _transfer.ExportAsync("bank_missing", new MemoryStream()));

        Assert.Equal("题库不存在，可能已被删除。", error.Message);
    }
}
