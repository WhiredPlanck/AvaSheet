using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using AvaSheet.Models;
using AvaSheet.ViewModels;
using AvaSheet.Views;

namespace AvaSheet.Services;

/// <summary>
/// 桌面端的弹窗实现。持有主窗口作为 owner，所以模态窗口居中、跟主窗口一起最小化。
/// </summary>
public sealed class DialogService(Window owner) : IDialogService
{
    private static readonly FilePickerFileType BankJsonType = new("题库文件")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"],
    };

    private static readonly FilePickerFileType QuestionFileType = new("题目文件")
    {
        Patterns = ["*.json", "*.xlsx", "*.xls", "*.xlsb"],
        MimeTypes =
        [
            "application/json",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "application/vnd.ms-excel",
            "application/vnd.ms-excel.sheet.binary.macroEnabled.12",
        ],
    };

    public async Task<string?> PromptBankNameAsync(
        string title,
        string initialValue = "",
        string confirmText = "确定")
    {
        var viewModel = new NameInputViewModel
        {
            Title = title,
            Value = initialValue,
            ConfirmText = confirmText,
        };

        var dialog = new NameInputWindow { DataContext = viewModel };
        return await dialog.ShowDialog<string?>(owner);
    }

    public async Task<bool> ShowBankManagerAsync(BankManagerViewModel viewModel)
    {
        var dialog = new BankManagerWindow { DataContext = viewModel };
        return await dialog.ShowDialog<bool>(owner);
    }

    public async Task<Question?> EditQuestionAsync(Question? existing)
    {
        var dialog = new QuestionEditorWindow
        {
            DataContext = new QuestionEditorViewModel(existing),
        };

        var saved = await dialog.ShowDialog<bool>(owner);
        return saved ? dialog.Result : null;
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText = "确定", bool destructive = false)
    {
        var dialog = new ConfirmWindow(title, message, confirmText, destructive);
        return dialog.ShowDialog<bool>(owner);
    }

    public async Task<IStorageFile?> PickImportFileAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入题库",
            AllowMultiple = false,
            FileTypeFilter = [BankJsonType],
        });

        return files.Count > 0 ? files[0] : null;
    }

    public Task<IStorageFile?> PickExportFileAsync(string suggestedFileName)
    {
        return owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出题库",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "json",
            FileTypeChoices = [BankJsonType],
            ShowOverwritePrompt = true,
        });
    }

    public async Task<IStorageFile?> PickQuestionImportFileAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入题目",
            AllowMultiple = false,
            FileTypeFilter = [QuestionFileType],
        });

        return files.Count > 0 ? files[0] : null;
    }

    public Task ShowImportResultAsync(ImportResultViewModel viewModel)
    {
        var dialog = new ImportResultWindow { DataContext = viewModel };
        return dialog.ShowDialog(owner);
    }

    public Task ShowExamResultAsync(ExamResultViewModel viewModel)
    {
        var dialog = new ExamResultWindow { DataContext = viewModel };
        return dialog.ShowDialog(owner);
    }

    public Task ShowAboutAsync()
    {
        var dialog = new AboutWindow();
        return dialog.ShowDialog(owner);
    }
}
