using AvaSheet.Models;
using AvaSheet.Services;
using Xunit;

namespace AvaSheet.Tests;

public sealed class ExamRecordStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _file;
    private readonly ExamRecordStore _store;

    public ExamRecordStoreTests()
    {
        _file = _temp.Combine("records.json");
        _store = new ExamRecordStore(_file);
    }

    public void Dispose() => _temp.Dispose();

    private static ExamRecord Record(string id, DateTimeOffset finishedAt, double score, double total) => new()
    {
        Id = id,
        BankName = "期末复习",
        StartedAt = finishedAt.AddMinutes(-10),
        FinishedAt = finishedAt,
        ElapsedSeconds = 600,
        LimitMinutes = 20,
        Score = score,
        Total = total,
        CorrectCount = 1,
        UnansweredCount = 1,
        Answers =
        [
            new ExamRecordAnswer
            {
                QuestionId = "q1",
                Type = QuestionType.SingleChoice,
                Stem = "单选题干",
                Selected = ["A"],
                Correct = ["A"],
                IsAnswered = true,
                IsCorrect = true,
            },
        ],
    };

    [Fact]
    public async Task ListAsync_ReturnsNewestFirst()
    {
        var now = DateTimeOffset.Now;
        await _store.SaveAsync(Record("old", now.AddHours(-2), 80, 100));
        await _store.SaveAsync(Record("new", now, 90, 100));
        await _store.SaveAsync(Record("middle", now.AddHours(-1), 70, 100));

        var records = await _store.ListAsync();

        Assert.Equal(new[] { "new", "middle", "old" }, records.Select(r => r.Id));
    }

    [Fact]
    public async Task SaveAsync_SameIdOverwritesInsteadOfAppending()
    {
        var record = Record("same", DateTimeOffset.Now, 80, 100);
        await _store.SaveAsync(record);

        record.Score = 95;
        await _store.SaveAsync(record);

        var saved = Assert.Single(await _store.ListAsync());
        Assert.Equal(95, saved.Score);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyThatRecord()
    {
        await _store.SaveAsync(Record("keep", DateTimeOffset.Now, 80, 100));
        await _store.SaveAsync(Record("drop", DateTimeOffset.Now, 90, 100));

        await _store.DeleteAsync("drop");

        var saved = Assert.Single(await _store.ListAsync());
        Assert.Equal("keep", saved.Id);
    }

    [Fact]
    public async Task ClearAsync_LeavesEmptyList()
    {
        await _store.SaveAsync(Record("a", DateTimeOffset.Now, 80, 100));

        await _store.ClearAsync();

        Assert.Empty(await _store.ListAsync());
        Assert.Equal("[]", (await File.ReadAllTextAsync(_file)).Trim());
    }

    [Fact]
    public async Task SaveAsync_DropsOldestBeyondLimit()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(8));
        for (var i = 0; i <= ExamRecordStore.MaxRecords; i++)
        {
            await _store.SaveAsync(Record($"r{i}", start.AddMinutes(i), i, 100));
        }

        var records = await _store.ListAsync();

        Assert.Equal(ExamRecordStore.MaxRecords, records.Count);
        Assert.DoesNotContain(records, r => r.Id == "r0");
        Assert.Equal("r200", records[0].Id);
    }

    [Fact]
    public async Task SaveAsync_WritesCamelCaseWithReadableChinese()
    {
        await _store.SaveAsync(Record("a", DateTimeOffset.Now, 80, 100));

        var text = await File.ReadAllTextAsync(_file);

        Assert.Contains("\"bankName\": \"期末复习\"", text);
        Assert.Contains("\"finishedAt\"", text);
        Assert.DoesNotContain("\\u", text);
    }

    [Fact]
    public async Task ListAsync_CorruptFileReturnsEmpty()
    {
        await File.WriteAllTextAsync(_file, "{ 坏了");
        var store = new ExamRecordStore(_file);

        Assert.Empty(await store.ListAsync());
    }
}
