using ExamSystem.Models;
using ExamSystem.Services;
using ExamSystem.ViewModels;
using Xunit;

namespace ExamSystem.Tests;

public sealed class PracticeViewModelTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly BankStore _store;
    private readonly BankProgressStore _progress;
    private readonly QuestionBank _bank;

    public PracticeViewModelTests()
    {
        _store = new BankStore(_temp.Combine("banks"));
        _progress = new BankProgressStore(_temp.Combine("progress"));
        _bank = TestData.Bank(
            TestData.Single("q_single", "单选题"),
            TestData.Multiple("q_multiple", "多选题", "A", "B"),
            TestData.TrueFalse("q_truefalse", "判断题"));
        _store.SaveAsync(_bank).GetAwaiter().GetResult();
    }

    public void Dispose() => _temp.Dispose();

    private async Task<PracticeViewModel> CreateAsync(
        bool startWrongOnly = false,
        Action? goBack = null,
        QuestionBank? bank = null)
    {
        var target = bank ?? _bank;
        var vm = new PracticeViewModel(_store, target.Id, target.Name, goBack, startWrongOnly, null, _progress);
        await vm.InitializeAsync();
        return vm;
    }

    private static PracticeTypeCardViewModel Card(PracticeViewModel vm, QuestionType type) =>
        vm.TypeCards.Single(c => c.Type == type);

    [Fact]
    public async Task Initialize_ShowsSetupWithOneCardPerType()
    {
        var vm = await CreateAsync();

        Assert.True(vm.IsSetup);
        Assert.Equal("平时练习", vm.Title);
        Assert.Equal(3, vm.TypeCards.Count);
        Assert.Equal(1, Card(vm, QuestionType.SingleChoice).Total);
        Assert.Equal(1, Card(vm, QuestionType.MultipleChoice).Total);
        Assert.Equal(1, Card(vm, QuestionType.TrueFalse).Total);
        Assert.True(Card(vm, QuestionType.SingleChoice).ShowStartButtons);
        Assert.Equal("共 1 题", Card(vm, QuestionType.SingleChoice).CountText);
    }

    [Fact]
    public async Task SingleChoice_JudgesAsSoonAsOptionIsPicked()
    {
        var vm = await CreateAsync();
        Card(vm, QuestionType.SingleChoice).StartSequentialCommand.Execute(null);

        Assert.True(vm.IsRunning);
        Assert.True(vm.IsAnswering);
        Assert.Equal(1, vm.Total);
        Assert.False(vm.ShowSubmit);

        vm.Options.Single(o => o.Key == "B").SelectCommand.Execute(null);

        Assert.True(vm.IsAnswered);
        Assert.False(vm.IsCorrectAnswer);
        Assert.Equal(1, vm.WrongCount);
        Assert.Equal("答错了", vm.ResultTitle);
        Assert.Equal("A", vm.CorrectAnswerText);
        Assert.Equal("正确答案：A", vm.ResultDetail);
        Assert.True(vm.Options.Single(o => o.Key == "A").IsCorrect);
        Assert.True(vm.Options.Single(o => o.Key == "B").IsWrongPick);

        vm.Options.Single(o => o.Key == "A").SelectCommand.Execute(null);

        Assert.False(vm.IsCorrectAnswer);
        Assert.Equal(1, vm.WrongCount);

        await vm.NextCommand.ExecuteAsync(null);

        Assert.True(vm.IsFinished);
        Assert.Equal(1, vm.WrongBookCount);
    }

    [Fact]
    public async Task MultipleChoice_NeedsEveryAnswerAndExplicitSubmit()
    {
        var vm = await CreateAsync();
        Card(vm, QuestionType.MultipleChoice).StartSequentialCommand.Execute(null);

        Assert.True(vm.ShowSubmit);
        Assert.False(vm.CanSubmit);

        vm.SubmitAnswerCommand.Execute(null);
        Assert.Equal("先选择答案，再提交。", vm.ErrorMessage);
        Assert.False(vm.IsAnswered);

        vm.Options.Single(o => o.Key == "A").SelectCommand.Execute(null);
        Assert.True(vm.CanSubmit);
        vm.SubmitAnswerCommand.Execute(null);

        Assert.True(vm.IsAnswered);
        Assert.False(vm.IsCorrectAnswer);
        Assert.Equal("A、B", vm.CorrectAnswerText);

        var second = await CreateAsync();
        Card(second, QuestionType.MultipleChoice).StartSequentialCommand.Execute(null);
        second.Options.Single(o => o.Key == "A").SelectCommand.Execute(null);
        second.Options.Single(o => o.Key == "B").SelectCommand.Execute(null);
        second.SubmitAnswerCommand.Execute(null);

        Assert.True(second.IsCorrectAnswer);
        Assert.Equal(1, second.CorrectCount);
    }

    [Fact]
    public async Task TrueFalse_UsesCorrectAndWrongOptions()
    {
        var vm = await CreateAsync();
        Card(vm, QuestionType.TrueFalse).StartSequentialCommand.Execute(null);

        Assert.Equal(new[] { "T", "F" }, vm.Options.Select(o => o.Key));
        Assert.Equal(new[] { "正确", "错误" }, vm.Options.Select(o => o.Text));

        vm.Options.Single(o => o.Key == "T").SelectCommand.Execute(null);

        Assert.True(vm.IsCorrectAnswer);
        Assert.Equal("正确", vm.CorrectAnswerText);
    }

    [Fact]
    public async Task WrongAnswer_GoesToWrongBookAndPracticeProgress()
    {
        var vm = await CreateAsync();
        Card(vm, QuestionType.SingleChoice).StartSequentialCommand.Execute(null);
        vm.Options.Single(o => o.Key == "B").SelectCommand.Execute(null);
        await vm.NextCommand.ExecuteAsync(null);

        var progress = await _progress.LoadAsync(_bank.Id);
        Assert.Contains("q_single", progress.WrongQuestionIds);
        var entry = Assert.Single(progress.PracticeProgress);
        Assert.Equal(QuestionType.SingleChoice, entry.Type);
        Assert.Contains("q_single", entry.DoneIds);

        var reopened = await CreateAsync();
        var card = Card(reopened, QuestionType.SingleChoice);
        Assert.Equal(1, reopened.WrongBookCount);
        Assert.Equal(1, card.DoneCount);
        Assert.Equal(0, card.Remaining);
        Assert.True(card.IsComplete);
        Assert.False(card.ShowStartButtons);
    }

    [Fact]
    public async Task CorrectAnswer_RemovesQuestionFromWrongBook()
    {
        await _progress.SaveAsync(_bank.Id, new BankProgress { WrongQuestionIds = ["q_single"] });

        var vm = await CreateAsync();
        Assert.Equal(1, vm.WrongBookCount);
        Card(vm, QuestionType.SingleChoice).StartSequentialCommand.Execute(null);
        vm.Options.Single(o => o.Key == "A").SelectCommand.Execute(null);
        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(0, vm.WrongBookCount);
        Assert.Empty((await _progress.LoadAsync(_bank.Id)).WrongQuestionIds);
    }

    [Fact]
    public async Task WrongOnlyRound_LeavesPracticeProgressUntouched()
    {
        await _progress.SaveAsync(_bank.Id, new BankProgress { WrongQuestionIds = ["q_single"] });

        var vm = await CreateAsync(startWrongOnly: true);

        Assert.True(vm.IsRunning);
        Assert.True(vm.IsWrongOnly);
        Assert.Equal("错题本", vm.Title);
        Assert.Equal(1, vm.Total);

        vm.Options.Single(o => o.Key == "A").SelectCommand.Execute(null);
        await vm.NextCommand.ExecuteAsync(null);

        Assert.True(vm.IsFinished);
        var progress = await _progress.LoadAsync(_bank.Id);
        Assert.Empty(progress.WrongQuestionIds);
        Assert.Empty(progress.PracticeProgress);
    }

    [Fact]
    public async Task EmptyWrongBook_ShowsEmptyPhaseWithoutError()
    {
        var vm = await CreateAsync(startWrongOnly: true);

        Assert.True(vm.IsEmpty);
        Assert.Null(vm.ErrorMessage);
        Assert.True(vm.ShowPracticeShortcut);
    }

    [Fact]
    public async Task ClearProgress_ResetsDoneCountForThatType()
    {
        var vm = await CreateAsync();
        Card(vm, QuestionType.SingleChoice).StartSequentialCommand.Execute(null);
        vm.Options.Single(o => o.Key == "A").SelectCommand.Execute(null);
        await vm.NextCommand.ExecuteAsync(null);
        await vm.SelectTypeCommand.ExecuteAsync(null);

        var card = Card(vm, QuestionType.SingleChoice);
        Assert.Equal(1, card.DoneCount);
        await card.ClearCommand.ExecuteAsync(null);
        var updated = Card(vm, QuestionType.SingleChoice);
        Assert.Equal(0, updated.DoneCount);
        Assert.True(updated.ShowStartButtons);
        Assert.DoesNotContain(
            (await _progress.LoadAsync(_bank.Id)).PracticeProgress,
            p => p.Type == QuestionType.SingleChoice);
    }

    [Fact]
    public async Task Leave_PersistsProgressAndGoesBack()
    {
        var left = false;
        var vm = await CreateAsync(goBack: () => left = true);
        Card(vm, QuestionType.SingleChoice).StartSequentialCommand.Execute(null);
        vm.Options.Single(o => o.Key == "B").SelectCommand.Execute(null);

        await vm.LeaveCommand.ExecuteAsync(null);

        Assert.True(left);
        Assert.Contains("q_single", (await _progress.LoadAsync(_bank.Id)).WrongQuestionIds);
    }

    [Fact]
    public async Task SequentialRound_FollowsBankOrder()
    {
        var bank = TestData.Bank(
            TestData.Single("s1", "第一题"),
            TestData.Single("s2", "第二题"),
            TestData.Single("s3", "第三题"));
        await _store.SaveAsync(bank);

        var vm = await CreateAsync(bank: bank);
        Card(vm, QuestionType.SingleChoice).StartSequentialCommand.Execute(null);

        var stems = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            stems.Add(vm.Stem);
            vm.Options.Single(o => o.Key == "A").SelectCommand.Execute(null);
            await vm.NextCommand.ExecuteAsync(null);
        }

        Assert.Equal(new[] { "第一题", "第二题", "第三题" }, stems);
    }
}
