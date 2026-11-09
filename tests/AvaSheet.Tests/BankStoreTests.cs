using System.Text;
using AvaSheet.Models;
using AvaSheet.Services;
using Xunit;

namespace AvaSheet.Tests;

public sealed class BankStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly BankProgressStore _progress;
    private readonly BankStore _store;

    public BankStoreTests()
    {
        _progress = new BankProgressStore(_temp.Combine("progress"));
        _store = new BankStore(_temp.Combine("banks"), _progress);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task SaveAndLoad_RoundTripsNameAndQuestions()
    {
        var bank = TestData.Bank(TestData.Single("q1"), TestData.TrueFalse("q2", answer: "F"));
        bank.Name = "  计算机基础  ";

        await _store.SaveAsync(bank);
        var loaded = await _store.LoadAsync(bank.Id);

        Assert.NotNull(loaded);
        Assert.Equal("计算机基础", loaded.Name);
        Assert.Equal(bank.Id, loaded.Id);
        Assert.Equal(2, loaded.Questions.Count);
        Assert.Equal(new[] { "A", "F" }, loaded.Questions.Select(q => q.Answers[0]));
    }

    [Fact]
    public async Task SaveAsync_WritesCurrentSchemaVersion()
    {
        var bank = TestData.Bank(TestData.Single("q1"));
        bank.SchemaVersion = 1;

        await _store.SaveAsync(bank);

        var text = await File.ReadAllTextAsync(Path.Combine(_temp.Combine("banks"), bank.Id + ".json"));
        Assert.Contains($"\"schemaVersion\": {QuestionBank.CurrentSchemaVersion}", text);
    }

    [Fact]
    public async Task CreateAsync_GeneratesIdAndTrimsName()
    {
        var id = await _store.CreateAsync("  新建题库  ", "说明");

        Assert.Matches("^bank_[0-9a-f]{12}$", id);
        var loaded = await _store.LoadAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal("新建题库", loaded.Name);
        Assert.Equal("说明", loaded.Description);
        Assert.Empty(loaded.Questions);
    }

    [Fact]
    public async Task SaveAsync_TruncatesLongName()
    {
        var bank = TestData.Bank();
        bank.Name = new string('题', 80);

        await _store.SaveAsync(bank);

        var loaded = await _store.LoadAsync(bank.Id);
        Assert.NotNull(loaded);
        Assert.Equal(60, loaded.Name.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_RejectsEmptyName(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.CreateAsync(name));
    }

    [Fact]
    public async Task LoadAsync_MissingBankReturnsNull()
    {
        Assert.Null(await _store.LoadAsync("bank_missing"));
    }

    [Fact]
    public async Task LoadAsync_RejectsBankFromNewerVersion()
    {
        var path = Path.Combine(_temp.Combine("banks"), "newer.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """{"schemaVersion":99,"name":"新版本","questions":[]}""");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => _store.LoadAsync("newer"));

        Assert.Contains("高于当前程序支持", error.Message);
    }

    [Fact]
    public async Task ListAsync_SummarizesQuestionsAndSkipsUnreadableFiles()
    {
        await _store.SaveAsync(TestData.Bank(TestData.Single("q1"), TestData.Single("q2")));

        var directory = _temp.Combine("banks");
        await File.WriteAllTextAsync(Path.Combine(directory, "broken.json"), "{ 不是 JSON");
        await File.WriteAllTextAsync(Path.Combine(directory, "bad id.json"), "{}");

        var summaries = await _store.ListAsync();

        var summary = Assert.Single(summaries);
        Assert.Equal("测试题库", summary.Name);
        Assert.Equal(2, summary.QuestionCount);
        Assert.Equal(2, _store.UnreadableFiles.Count);
        Assert.Contains("broken.json", _store.UnreadableFiles);
        Assert.Contains("bad id.json", _store.UnreadableFiles);
    }

    [Fact]
    public async Task RenameAsync_KeepsQuestions()
    {
        var bank = TestData.Bank(TestData.Single("q1"));
        await _store.SaveAsync(bank);

        await _store.RenameAsync(bank.Id, "  改名后的题库  ");

        var loaded = await _store.LoadAsync(bank.Id);
        Assert.NotNull(loaded);
        Assert.Equal("改名后的题库", loaded.Name);
        Assert.Single(loaded.Questions);
    }

    [Fact]
    public async Task DeleteAsync_RemovesBankAndItsProgress()
    {
        var bank = TestData.Bank(TestData.Single("q1"));
        await _store.SaveAsync(bank);
        await _progress.SaveAsync(bank.Id, new BankProgress { WrongQuestionIds = ["q1"] });

        await _store.DeleteAsync(bank.Id);

        Assert.Null(await _store.LoadAsync(bank.Id));
        Assert.Empty((await _progress.LoadAsync(bank.Id)).WrongQuestionIds);
        Assert.False(File.Exists(Path.Combine(_temp.Combine("progress"), bank.Id + ".json")));
    }

    [Fact]
    public async Task SaveAsync_LeavesNoTemporaryFile()
    {
        await _store.SaveAsync(TestData.Bank(TestData.Single("q1")));

        Assert.Empty(Directory.GetFiles(_temp.Combine("banks"), "*.tmp"));
    }
}

public sealed class BankProgressStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly BankProgressStore _store;

    public BankProgressStoreTests() => _store = new BankProgressStore(_temp.Combine("progress"));

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task SaveAndLoad_RoundTripsWrongBookAndPracticeProgress()
    {
        await _store.SaveAsync("bank_a", new BankProgress
        {
            WrongQuestionIds = ["q1", "q2"],
            PracticeProgress =
            [
                new PracticeProgressEntry { Type = QuestionType.SingleChoice, DoneIds = ["q1"] },
            ],
        });

        var loaded = await _store.LoadAsync("bank_a");

        Assert.Equal(new[] { "q1", "q2" }, loaded.WrongQuestionIds);
        var entry = Assert.Single(loaded.PracticeProgress);
        Assert.Equal(QuestionType.SingleChoice, entry.Type);
        Assert.Equal(new[] { "q1" }, entry.DoneIds);
    }

    [Fact]
    public async Task SaveAsync_EmptyProgressDeletesFile()
    {
        await _store.SaveAsync("bank_a", new BankProgress { WrongQuestionIds = ["q1"] });
        var path = Path.Combine(_temp.Combine("progress"), "bank_a.json");
        Assert.True(File.Exists(path));

        await _store.SaveAsync("bank_a", new BankProgress());

        Assert.False(File.Exists(path));
        Assert.Empty((await _store.LoadAsync("bank_a")).WrongQuestionIds);
    }

    [Fact]
    public async Task LoadAsync_CorruptFileReturnsEmptyProgress()
    {
        var directory = _temp.Combine("progress");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "bank_a.json"), "{ 坏了");

        var loaded = await _store.LoadAsync("bank_a");

        Assert.Empty(loaded.WrongQuestionIds);
        Assert.Empty(loaded.PracticeProgress);
    }

    [Fact]
    public async Task InvalidBankId_IsRejectedOnSaveAndIgnoredOnLoad()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _store.SaveAsync("../escape", new BankProgress { WrongQuestionIds = ["q1"] }));

        Assert.Empty((await _store.LoadAsync("../escape")).WrongQuestionIds);
    }
}

public sealed class AtomicFileTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task WriteAllTextAsync_OverwritesAtomicallyWithoutBom()
    {
        var path = _temp.Combine("data.json");

        await AtomicFile.WriteAllTextAsync(path, "第一版");
        await AtomicFile.WriteAllTextAsync(path, "第二版：中文");

        Assert.Equal("第二版：中文", await File.ReadAllTextAsync(path, Encoding.UTF8));
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.False(File.Exists(path + ".tmp"));
    }
}
