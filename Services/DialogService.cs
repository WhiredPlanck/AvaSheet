using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ExamSystem.ViewModels;
using ExamSystem.Views;

namespace ExamSystem.Services;

/// <summary>
/// 桌面端的弹窗实现。持有主窗口作为 owner，所以模态窗口居中、跟主窗口一起最小化。
/// </summary>
public sealed class DialogService : IDialogService
{
    private static readonly FilePickerFileType BankJsonType = new("题库文件")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"],
    };

    private readonly Window _owner;

    public DialogService(Window owner)
    {
        _owner = owner;
    }

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
        return await dialog.ShowDialog<string?>(_owner);
    }

    public async Task<bool> ShowBankManagerAsync(BankManagerViewModel viewModel)
    {
        var dialog = new BankManagerWindow { DataContext = viewModel };
        return await dialog.ShowDialog<bool>(_owner);
    }

    public async Task<IStorageFile?> PickImportFileAsync()
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入题库",
            AllowMultiple = false,
            FileTypeFilter = [BankJsonType],
        });

        return files.Count > 0 ? files[0] : null;
    }

    public Task<IStorageFile?> PickExportFileAsync(string suggestedFileName)
    {
        return _owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出题库",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "json",
            FileTypeChoices = [BankJsonType],
            ShowOverwritePrompt = true,
        });
    }
}
