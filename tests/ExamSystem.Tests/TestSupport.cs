using System.Data;
using Avalonia.Platform.Storage;
using ExamSystem.Models;
using ExamSystem.Services;
using ExamSystem.ViewModels;
using Sylvan.Data.Excel;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ExamSystem.Tests;

/// <summary>测试期间关掉并行：ViewModel 里有计时器，共享 UI 线程的假设在并行下拉不开。</summary>
/// <summary>每个测试一份独立的用户数据目录，用完删掉。</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "examsystem-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Combine(string name) => Path.Combine(Root, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时目录删不掉不影响测试结果。
        }
    }
}

/// <summary>造题库与题目，让每个测试只看自己关心的字段。</summary>
internal static class TestData
{
    public static Question Single(string id, string stem = "单选题", string answer = "A") => new()
    {
        Id = id,
        Type = QuestionType.SingleChoice,
        Stem = stem,
        Choices = [new QuestionChoice { Key = "A", Text = "甲" }, new QuestionChoice { Key = "B", Text = "乙" }],
        Answers = [answer],
    };

    public static Question Multiple(string id, string stem = "多选题", params string[] answers) => new()
    {
        Id = id,
        Type = QuestionType.MultipleChoice,
        Stem = stem,
        Choices =
        [
            new QuestionChoice { Key = "A", Text = "甲" },
            new QuestionChoice { Key = "B", Text = "乙" },
            new QuestionChoice { Key = "C", Text = "丙" },
        ],
        Answers = [.. answers],
    };

    public static Question TrueFalse(string id, string stem = "判断题", string answer = "T") => new()
    {
        Id = id,
        Type = QuestionType.TrueFalse,
        Stem = stem,
        Answers = [answer],
    };

    public static QuestionBank Bank(params Question[] questions) => new()
    {
        Id = "bank_test",
        Name = "测试题库",
        Questions = [.. questions],
    };

    /// <summary>一份覆盖三种题型的小题库：单选、多选（A、B）、判断（T）。</summary>
    public static QuestionBank MixedBank() => Bank(
        Single("q_single", "单选题干"),
        Multiple("q_multiple", "多选题干", "A", "B"),
        TrueFalse("q_truefalse", "判断题干"));
}

/// <summary>记录弹窗调用，并按需返回预设结果。</summary>
internal sealed class StubDialogs : IDialogService
{
    public List<ExamResultViewModel> Results { get; } = [];

    public List<(string Title, string Message, string ConfirmText, bool Destructive)> Confirms { get; } = [];

    public bool ConfirmResult { get; set; } = true;

    public string? LastConfirmTitle => Confirms.Count == 0 ? null : Confirms[^1].Title;

    public string? LastConfirmMessage => Confirms.Count == 0 ? null : Confirms[^1].Message;

    public Task<string?> PromptBankNameAsync(string title, string initialValue = "", string confirmText = "确定")
        => Task.FromResult<string?>(null);

    public Task<bool> ShowBankManagerAsync(BankManagerViewModel viewModel) => Task.FromResult(false);

    public Task<Question?> EditQuestionAsync(Question? existing) => Task.FromResult<Question?>(null);

    public Task<bool> ConfirmAsync(string title, string message, string confirmText = "确定", bool destructive = false)
    {
        Confirms.Add((title, message, confirmText, destructive));
        return Task.FromResult(ConfirmResult);
    }

    public Task<IStorageFile?> PickImportFileAsync() => Task.FromResult<IStorageFile?>(null);

    public Task<IStorageFile?> PickQuestionImportFileAsync() => Task.FromResult<IStorageFile?>(null);

    public Task ShowImportResultAsync(ImportResultViewModel viewModel) => Task.CompletedTask;

    public Task ShowExamResultAsync(ExamResultViewModel viewModel)
    {
        Results.Add(viewModel);
        return Task.CompletedTask;
    }

    public Task ShowAboutAsync() => Task.CompletedTask;

    public Task<IStorageFile?> PickExportFileAsync(string suggestedFileName) => Task.FromResult<IStorageFile?>(null);
}

internal static class TestWorkbook
{
    /// <summary>把二维文本写成一个 Excel 工作簿，供导入测试读取。</summary>
    public static MemoryStream Create(ExcelWorkbookType type, params string[][] rows)
    {
        var stream = new MemoryStream();
        using (var writer = ExcelDataWriter.Create(stream, type, new ExcelDataWriterOptions { OwnsStream = false }))
        {
            var width = rows.Max(row => row.Length);
            var table = new DataTable();
            for (var i = 0; i < width; i++)
            {
                table.Columns.Add();
            }

            foreach (var row in rows)
            {
                var values = new object?[width];
                for (var i = 0; i < width; i++)
                {
                    values[i] = i < row.Length ? row[i] : null;
                }

                table.Rows.Add(values);
            }

            writer.Write(table.CreateDataReader());
        }

        stream.Position = 0;
        return stream;
    }
}
