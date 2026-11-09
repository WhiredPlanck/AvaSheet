using AvaSheet.Models;
using AvaSheet.Services;
using AvaSheet.ViewModels;
using Xunit;

namespace AvaSheet.Tests;

public sealed class ExamRecordsViewModelTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _file;
    private readonly ExamRecordStore _store;
    private readonly StubDialogs _dialogs = new();

    public ExamRecordsViewModelTests()
    {
        _file = _temp.Combine("records.json");
        _store = new ExamRecordStore(_file);
    }

    public void Dispose() => _temp.Dispose();

    private static ExamRecord Record(
        string id,
        double score,
        double total,
        DateTimeOffset finishedAt,
        bool autoSubmitted = false,
        int unanswered = 0) => new()
        {
            Id = id,
            BankId = "bank_test",
            BankName = "测试题库",
            StartedAt = finishedAt.AddMinutes(-12),
            FinishedAt = finishedAt,
            ElapsedSeconds = 730,
            LimitMinutes = 30,
            AutoSubmitted = autoSubmitted,
            Score = score,
            Total = total,
            CorrectCount = unanswered == 0 ? 2 : 0,
            UnansweredCount = unanswered,
            Answers =
            [
                new ExamRecordAnswer
                {
                    QuestionId = "q1",
                    Type = QuestionType.SingleChoice,
                    Stem = "单选",
                    Selected = ["A"],
                    Correct = ["A"],
                    IsAnswered = true,
                    IsCorrect = true,
                    Score = 1,
                    Explanation = "基础算术",
                },
                new ExamRecordAnswer
                {
                    QuestionId = "q2",
                    Type = QuestionType.MultipleChoice,
                    Stem = "多选",
                    Selected = ["A"],
                    Correct = ["A", "B"],
                    IsAnswered = true,
                    IsCorrect = false,
                    Score = 2,
                },
            ],
        };

    private async Task<ExamRecordsViewModel> CreateAsync()
    {
        var vm = new ExamRecordsViewModel(_store, _dialogs);
        await vm.InitializeAsync();
        return vm;
    }

    [Fact]
    public async Task Initialize_SummarizesRecordsNewestFirst()
    {
        await _store.SaveAsync(Record("r1", 18, 20, new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.FromHours(8)), autoSubmitted: true, unanswered: 2));
        await _store.SaveAsync(Record("r2", 10, 10, new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.FromHours(8))));

        var vm = await CreateAsync();

        Assert.True(vm.HasRecords);
        Assert.Equal("2 次", vm.CountText);
        Assert.Equal("93%", vm.AverageText);
        Assert.Equal("10 / 10", vm.RecentText);
        Assert.Equal("r2", vm.Records[0].Record.Id);

        var older = vm.Records[1];
        Assert.Equal("测试题库", older.BankName);
        Assert.Equal("2 题 · 限时 30 分钟 · 12 分 10 秒", older.MetaText);
        Assert.Equal("时间到自动交卷 · 2 题未答", older.NoteText);
        Assert.True(older.HasNote);
        Assert.Equal("90%", older.AccuracyText);
    }

    [Fact]
    public async Task Row_ExpandsAndDescribesEachAnswer()
    {
        await _store.SaveAsync(Record("r1", 18, 20, DateTimeOffset.Now));
        var vm = await CreateAsync();
        var row = Assert.Single(vm.Records);

        Assert.False(row.IsExpanded);
        Assert.Equal("展开", row.ExpandText);
        row.ToggleCommand.Execute(null);
        Assert.True(row.IsExpanded);
        Assert.Equal("收起", row.ExpandText);

        var correct = row.Answers[0];
        Assert.Equal("答对", correct.ResultText);
        Assert.Equal("你的答案：A", correct.YourAnswerText);
        Assert.Equal("正确答案：A", correct.CorrectAnswerText);
        Assert.True(correct.HasExplanation);
        Assert.Equal("解析：基础算术", correct.ExplanationText);

        var wrong = row.Answers[1];
        Assert.Equal("答错", wrong.ResultText);
        Assert.True(wrong.IsWrong);
        Assert.Equal("正确答案：A、B", wrong.CorrectAnswerText);
        Assert.False(wrong.HasExplanation);
    }

    [Fact]
    public async Task DeleteRow_AsksForConfirmation()
    {
        await _store.SaveAsync(Record("r1", 18, 20, DateTimeOffset.Now));
        await _store.SaveAsync(Record("r2", 10, 10, DateTimeOffset.Now.AddMinutes(1)));
        var vm = await CreateAsync();

        _dialogs.ConfirmResult = false;
        await vm.Records[0].DeleteCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Records.Count);
        Assert.Single(_dialogs.Confirms);

        _dialogs.ConfirmResult = true;
        await vm.Records[0].DeleteCommand.ExecuteAsync(null);

        Assert.Single(vm.Records);
        Assert.Equal("1 次", vm.CountText);
        var confirm = _dialogs.Confirms[^1];
        Assert.Equal("删除考试记录", confirm.Title);
        Assert.Contains("删除后不能恢复", confirm.Message);
        Assert.True(confirm.Destructive);
        Assert.Equal("r1", Assert.Single(await _store.ListAsync()).Id);
    }

    [Fact]
    public async Task ClearAll_EmptiesStoreAndShowsEmptyPhase()
    {
        await _store.SaveAsync(Record("r1", 18, 20, DateTimeOffset.Now));
        var vm = await CreateAsync();

        await vm.ClearAllCommand.ExecuteAsync(null);

        Assert.True(vm.IsEmpty);
        Assert.Equal("0 次", vm.CountText);
        Assert.Equal("—", vm.RecentText);
        Assert.Empty(await _store.ListAsync());
        Assert.Equal("清空考试记录", _dialogs.Confirms[^1].Title);
        Assert.True(_dialogs.Confirms[^1].Destructive);
    }

    [Fact]
    public async Task Initialize_WithoutStoreShowsEmpty()
    {
        var vm = new ExamRecordsViewModel(null, null);
        await vm.InitializeAsync();

        Assert.True(vm.IsEmpty);
    }
}
