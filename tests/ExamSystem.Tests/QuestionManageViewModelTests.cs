using System.Text;
using ExamSystem.Models;
using ExamSystem.Services;
using ExamSystem.ViewModels;
using Sylvan.Data.Excel;
using Xunit;

namespace ExamSystem.Tests;

public sealed class QuestionManageViewModelTests : IDisposable
{
    private const string TwoQuestionsJson = """
        [
          {
            "type": "SingleChoice",
            "stem": "首都是哪里",
            "choices": [{ "key": "A", "text": "北京" }, { "key": "B", "text": "上海" }],
            "answers": ["A"],
            "difficulty": 1
          },
          {
            "type": "TrueFalse",
            "stem": "地球是圆的",
            "choices": [],
            "answers": ["T"],
            "difficulty": 3
          }
        ]
        """;

    private readonly TempDirectory _temp = new();
    private readonly BankStore _store;
    private readonly StubDialogs _dialogs = new();
    private readonly QuestionBank _bank;

    public QuestionManageViewModelTests()
    {
        _store = new BankStore(_temp.Combine("banks"));
        _bank = TestData.Bank(TestData.Single("q1", "已有题目"));
        _store.SaveAsync(_bank).GetAwaiter().GetResult();
    }

    public void Dispose() => _temp.Dispose();

    private async Task<QuestionManageViewModel> CreateAsync()
    {
        var vm = new QuestionManageViewModel(_store, _dialogs, _bank.Id, _bank.Name);
        await vm.InitializeAsync();
        return vm;
    }

    private static MemoryStream Json(string json) => new(Encoding.UTF8.GetBytes(json));

    [Fact]
    public async Task Initialize_ListsQuestionsWithStats()
    {
        var vm = await CreateAsync();

        Assert.True(vm.HasBank);
        Assert.Single(vm.Questions);
        Assert.Equal("已有题目", vm.Questions[0].Stem);
        Assert.Equal("共 1 道题 · 单选 1 · 多选 0 · 判断 0", vm.StatsText);
        Assert.False(vm.IsEmptyBank);
        Assert.False(vm.NoMatch);
    }

    [Fact]
    public async Task SearchAndFilter_NarrowTheList()
    {
        var vm = await CreateAsync();
        using var stream = Json(TwoQuestionsJson);
        await vm.ImportFromStreamAsync(stream, "questions.json");

        vm.SearchText = "首都";
        Assert.Single(vm.Questions);
        Assert.Equal("首都是哪里", vm.Questions[0].Stem);

        vm.SearchText = "不存在的关键词";
        Assert.Empty(vm.Questions);
        Assert.True(vm.NoMatch);

        vm.SearchText = "";
        vm.Filter = QuestionFilterKind.TrueFalse;
        Assert.Single(vm.Questions);
        Assert.Equal(QuestionType.TrueFalse, vm.Questions[0].Question.Type);
    }

    [Fact]
    public async Task Import_AsksForConfirmationThenAppendsQuestions()
    {
        var vm = await CreateAsync();
        using var stream = Json(TwoQuestionsJson);

        var result = await vm.ImportFromStreamAsync(stream, "questions.json");

        var confirm = Assert.Single(_dialogs.Confirms);
        Assert.Equal("导入题目", confirm.Title);
        Assert.Contains("文件里有 2 道题，可以导入 2 道。", confirm.Message);
        Assert.Contains("导入后会追加到当前题库末尾。", confirm.Message);

        Assert.NotNull(result);
        Assert.Equal("已导入 2 道题。", result.SummaryText);
        Assert.False(result.HasSkipped);
        Assert.Equal(3, vm.Questions.Count);

        var reloaded = await _store.LoadAsync(_bank.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(3, reloaded!.Questions.Count);
    }

    [Fact]
    public async Task Import_SkipsDuplicatesFromBankAndSameBatch()
    {
        var vm = await CreateAsync();
        using var first = Json(TwoQuestionsJson);
        await vm.ImportFromStreamAsync(first, "questions.json");

        using var again = Json(TwoQuestionsJson);
        var result = await vm.ImportFromStreamAsync(again, "questions.json");

        Assert.NotNull(result);
        Assert.Equal("没有导入任何题目，2 道重复题。", result.SummaryText);
        Assert.Single(_dialogs.Confirms);
        Assert.Equal(3, vm.Questions.Count);
    }

    [Fact]
    public async Task Import_TreatsStemsThatDifferOnlyInWhitespaceAndCaseAsDuplicate()
    {
        var vm = await CreateAsync();

        using var stream = Json("""
            [
              { "type": "SingleChoice", "stem": "  已有题目 ", "choices": [{ "key": "A", "text": "甲" }, { "key": "B", "text": "乙" }], "answers": ["A"] },
              { "type": "SingleChoice", "stem": "新题目", "choices": [{ "key": "A", "text": "甲" }, { "key": "B", "text": "乙" }], "answers": ["A"] },
              { "type": "SingleChoice", "stem": "  新题目 ", "choices": [{ "key": "A", "text": "甲" }, { "key": "B", "text": "乙" }], "answers": ["A"] }
            ]
            """);

        var result = await vm.ImportFromStreamAsync(stream, "questions.json");

        Assert.NotNull(result);
        Assert.Equal("已导入 1 道题，跳过 2 道重复题。", result.SummaryText);
        Assert.Equal(2, vm.Questions.Count);
    }

    [Fact]
    public async Task Import_CancelledLeavesBankUntouched()
    {
        var vm = await CreateAsync();
        _dialogs.ConfirmResult = false;
        using var stream = Json(TwoQuestionsJson);

        var result = await vm.ImportFromStreamAsync(stream, "questions.json");

        Assert.Null(result);
        Assert.Single(vm.Questions);
        Assert.Single((await _store.LoadAsync(_bank.Id))!.Questions);
    }

    [Fact]
    public async Task Import_EmptyJsonThrows()
    {
        var vm = await CreateAsync();
        using var stream = Json("[]");

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => vm.ImportFromStreamAsync(stream, "questions.json"));

        Assert.Equal("文件里没有可导入的题目。", error.Message);
    }

    [Fact]
    public async Task Import_ReadsExcelThroughTheSameEntryPoint()
    {
        var vm = await CreateAsync();
        using var workbook = TestWorkbook.Create(
            ExcelWorkbookType.ExcelXml,
            ["类型", "题干", "A", "B", "答案", "难度"],
            ["单选题", "表格里的题目", "甲", "乙", "A", "简单"]);

        var result = await vm.ImportFromStreamAsync(workbook, "questions.xlsx");

        Assert.NotNull(result);
        Assert.Equal("已导入 1 道题。", result.SummaryText);
        Assert.Contains(vm.Questions, q => q.Stem == "表格里的题目");
        Assert.Contains("表格里识别出 1 道题", _dialogs.Confirms[0].Message);
    }
}
