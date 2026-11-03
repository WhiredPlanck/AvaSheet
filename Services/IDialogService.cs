using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using ExamSystem.Models;
using ExamSystem.ViewModels;

namespace ExamSystem.Services;

/// <summary>
/// 把「弹窗」和「选文件」从 ViewModel 里抽出来，ViewModel 只描述意图。
/// 实现放在 <see cref="DialogService"/>，由 App 在组装时注入。
/// </summary>
public interface IDialogService
{
    /// <summary>弹一个输入框问题库名称。取消返回 null。</summary>
    Task<string?> PromptBankNameAsync(string title, string initialValue = "", string confirmText = "确定");

    /// <summary>打开题库管理窗口，返回是否改动过数据。</summary>
    Task<bool> ShowBankManagerAsync(BankManagerViewModel viewModel);

    /// <summary>打开题目编辑器。保存返回编辑后的题目，取消返回 null。</summary>
    Task<Question?> EditQuestionAsync(Question? existing);

    /// <summary>通用确认框，确认返回 true。</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "确定", bool destructive = false);

    /// <summary>选一个要导入的 JSON 文件。</summary>
    Task<IStorageFile?> PickImportFileAsync();

    /// <summary>选一个要导入的题目文件，可选 JSON 或 Excel。</summary>
    Task<IStorageFile?> PickQuestionImportFileAsync();

    /// <summary>显示导入结果的明细窗。</summary>
    Task ShowImportResultAsync(ImportResultViewModel viewModel);

    /// <summary>显示「关于」窗口：图标、软件名称和版本。</summary>
    Task ShowAboutAsync();

    /// <summary>选导出目标，返回 null 表示用户取消。</summary>
    Task<IStorageFile?> PickExportFileAsync(string suggestedFileName);
}
