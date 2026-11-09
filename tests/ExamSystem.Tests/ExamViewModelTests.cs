using ExamSystem.Models;
using ExamSystem.Services;
using ExamSystem.ViewModels;
using Xunit;

namespace ExamSystem.Tests;

public sealed class ExamViewModelTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly BankStore _store;
    private readonly BankProgressStore _progress;
    private readonly StubDialogs _dialogs = new();
    private readonly QuestionBank _bank;

    public ExamViewModelTests()
    {
        _store = new BankStore(_temp.Combine("banks"));
        _progress = new BankProgressStore(_temp.Combine("progress"));
        _bank = TestData.Bank(
            TestData.Single("q_single1", "单选一"),
            TestData.Single("q_single2", "单选二"),
            TestData.Multiple("q_multiple1", "多选一", "A", "B"),
            TestData.Multiple("q_multiple2", "多选二", "B", "C"),
            TestData.TrueFalse("q_truefalse", "判断题"));
        _store.SaveAsync(_bank).GetAwaiter().GetResult();
    }

    public void Dispose() => _temp.Dispose();

    private async Task<ExamViewModel> CreateAsync()
    {
        var vm = new ExamViewModel(_store, _bank.Id, _bank.Name, _dialogs, null, null, _progress);
        await vm.InitializeAsync();
        return vm;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(condition(), "等待异步操作超时");
    }

    [Fact]
    public async Task Initialize_EntersConfigWithDefaultsFilledFromBank()
    {
        var vm = await CreateAsync();

        Assert.True(vm.IsConfig);
        Assert.True(vm.SingleEnabled);
        Assert.True(vm.MultipleEnabled);
        Assert.True(vm.TrueFalseEnabled);
        Assert.Equal(2, vm.SingleCount);
        Assert.Equal(2, vm.MultipleCount);
        Assert.Equal(1, vm.TrueFalseCount);
        Assert.Equal(5, vm.PaperSize);
        Assert.True(vm.CanStart);
    }

    [Fact]
    public async Task TypeCheckBox_SyncsWithCountInBothDirections()
    {
        var vm = await CreateAsync();

        vm.SingleEnabled = false;
        Assert.Equal(0, vm.SingleCount);
        Assert.Equal(3, vm.PaperSize);
        Assert.Contains("共 3 题", vm.PaperTotalsText);

        vm.SingleEnabled = true;
        Assert.Equal(2, vm.SingleCount);

        vm.SingleCountValue = 1;
        vm.SingleEnabled = false;
        vm.SingleEnabled = true;
        Assert.Equal(1, vm.SingleCount);

        vm.SingleCountValue = 0;
        Assert.False(vm.SingleEnabled);
        vm.SingleCountValue = 2;
        Assert.True(vm.SingleEnabled);

        vm.SingleEnabled = false;
        vm.MultipleEnabled = false;
        vm.TrueFalseEnabled = false;
        Assert.Equal(0, vm.PaperSize);
        Assert.False(vm.CanStart);
        Assert.Contains("至少给一种题型", vm.PaperTotalsText);
    }

    [Fact]
    public async Task Start_DrawsOnlyEnabledQuestionTypes()
    {
        var vm = await CreateAsync();
        vm.TrueFalseEnabled = false;

        vm.StartCommand.Execute(null);

        Assert.True(vm.IsRunning);
        Assert.True(vm.IsAnswering);
        Assert.False(vm.ShowPageHeader);
        Assert.Equal(4, vm.Questions.Count);
        Assert.DoesNotContain(vm.Questions, q => q.Question.Type == QuestionType.TrueFalse);
    }

    [Fact]
    public async Task Submit_FreezesPaperAndShowsResultWindow()
    {
        var vm = await CreateAsync();
        vm.StartCommand.Execute(null);

        var single = vm.Questions.Single(q => q.Question.Id == "q_single1");
        var multiple = vm.Questions.Single(q => q.Question.Id == "q_multiple1");
        single.Options.Single(o => o.Key == "A").SelectCommand.Execute(null);
        multiple.Options.Single(o => o.Key == "A").SelectCommand.Execute(null);

        await vm.SubmitCommand.ExecuteAsync(null);

        Assert.True(vm.IsFinished);
        Assert.True(vm.IsAnswering);
        Assert.True(vm.CanExitWithoutPrompt);
        Assert.Equal(1.0, vm.Score);
        Assert.Equal("答对 1 题 · 答错 1 题 · 未答 3 题", vm.ScoreSummary);

        var result = Assert.Single(_dialogs.Results);
        Assert.Equal("1", result.ScoreText);
        Assert.Equal("/ 5", result.TotalText);
        Assert.Equal("20%", result.AccuracyText);
        Assert.Equal("答对 1 题 · 答错 1 题 · 未答 3 题", result.Summary);
        Assert.Equal("有 3 题未作答，按 0 分计。", result.UnansweredText);
        Assert.Equal("本次答错的 1 道题已加入错题本。", result.WrongBookNote);
        Assert.StartsWith("用时", result.ElapsedText);

        var confirm = Assert.Single(_dialogs.Confirms);
        Assert.Equal("还有题目没答", confirm.Title);
        Assert.Equal("还有 3 题没有作答，确定要交卷吗？", confirm.Message);
    }

    [Fact]
    public async Task FinishedPaper_IgnoresFurtherClicksAndShowsAnswers()
    {
        var vm = await CreateAsync();
        vm.StartCommand.Execute(null);

        var multiple = vm.Questions.Single(q => q.Question.Id == "q_multiple1");
        multiple.Options.Single(o => o.Key == "A").SelectCommand.Execute(null);
        await vm.SubmitCommand.ExecuteAsync(null);

        multiple.Options.Single(o => o.Key == "B").SelectCommand.Execute(null);

        Assert.Equal(new[] { "A" }, multiple.Options.Where(o => o.IsSelected).Select(o => o.Key));
        Assert.All(multiple.Options, o => Assert.False(o.IsActive));
        Assert.Equal("答错了", multiple.ResultTitle);
        Assert.Equal("正确答案：A、B", multiple.ResultDetail);
        Assert.True(multiple.Options.Single(o => o.Key == "B").IsMissed);
        Assert.False(multiple.Options.Single(o => o.Key == "A").IsWrongPick);

        var skipped = vm.Questions.Single(q => q.Question.Id == "q_truefalse");
        Assert.Equal("未作答", skipped.ResultTitle);
        Assert.Equal("正确答案：正确", skipped.ResultDetail);
        Assert.False(skipped.HasExplanation);

        Assert.True(vm.Navigator.Single(n => n.Number == multiple.Number).IsWrong);
        Assert.True(vm.Navigator.Single(n => n.Number == skipped.Number).IsSkipped);
    }

    [Fact]
    public async Task Submit_AddsWrongAnswersToWrongBookOnly()
    {
        var vm = await CreateAsync();
        vm.StartCommand.Execute(null);

        vm.Questions.Single(q => q.Question.Id == "q_single1")
            .Options.Single(o => o.Key == "B").SelectCommand.Execute(null);
        await vm.SubmitCommand.ExecuteAsync(null);

        var progress = await _progress.LoadAsync(_bank.Id);
        Assert.Contains("q_single1", progress.WrongQuestionIds);
        Assert.DoesNotContain("q_truefalse", progress.WrongQuestionIds);
    }

    [Fact]
    public async Task ShowResult_ReopensWindowAndRestartReturnsToConfig()
    {
        var vm = await CreateAsync();
        vm.StartCommand.Execute(null);
        await vm.SubmitCommand.ExecuteAsync(null);

        await vm.ShowResultCommand.ExecuteAsync(null);

        Assert.Equal(2, _dialogs.Results.Count);
        Assert.True(vm.IsFinished);

        vm.RestartCommand.Execute(null);

        Assert.True(vm.IsConfig);
        Assert.False(vm.IsAnswering);
    }

    [Fact]
    public async Task Timeout_AutoSubmitsAllUnanswered()
    {
        var vm = await CreateAsync();
        vm.StartCommand.Execute(null);

        vm.TickForTest(TimeSpan.FromMinutes(vm.LimitMinutes + 1));
        await WaitUntilAsync(() => vm.IsFinished);

        Assert.Equal("时间到，已自动交卷。", vm.ErrorMessage);
        var result = Assert.Single(_dialogs.Results);
        Assert.Equal("0", result.ScoreText);
        Assert.Equal("答对 0 题 · 答错 0 题 · 未答 5 题", result.Summary);
        Assert.Empty(_dialogs.Confirms);
    }
}
