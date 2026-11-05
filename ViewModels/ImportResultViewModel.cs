using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ExamSystem.Services;

namespace ExamSystem.ViewModels;

/// <summary>导入结束后的小窗内容：导入了多少，跳过了什么、为什么。</summary>
public sealed class ImportResultViewModel : ViewModelBase
{
    public ImportResultViewModel(int importedCount, IReadOnlyList<ExcelRowError> errors, int duplicateCount = 0)
    {
        Errors = new ObservableCollection<ImportErrorRowViewModel>(errors.Select(e => new ImportErrorRowViewModel(e)));

        var skipped = new List<string>();
        if (duplicateCount > 0)
        {
            skipped.Add($"{duplicateCount} 道重复题");
        }

        if (Errors.Count > 0)
        {
            skipped.Add($"{Errors.Count} 行无法识别");
        }

        var skipText = skipped.Count == 0 ? "" : string.Join("、", skipped);

        SummaryText = importedCount > 0
            ? $"已导入 {importedCount} 道题" + (skipText.Length == 0 ? "。" : $"，跳过 {skipText}。")
            : skipText.Length == 0
                ? "没有可导入的题目。"
                : $"没有导入任何题目，{skipText}。";

        HasSkipped = skipped.Count > 0;
    }

    public string Title => "导入结果";

    public string SummaryText { get; }

    public ObservableCollection<ImportErrorRowViewModel> Errors { get; }

    public bool HasErrors => Errors.Count > 0;

    /// <summary>有重复题或报错行时为真，用来显示下面那句「改好表格后可以重新导入」。</summary>
    public bool HasSkipped { get; }

    public string HintText => HasSkipped ? "已导入的题目不会重复进来，可以改好表格后再导一次。" : "";
}

/// <summary>一行导入失败的说明。</summary>
public sealed class ImportErrorRowViewModel
{
    public ImportErrorRowViewModel(ExcelRowError error)
    {
        Row = error.Row;
        Message = error.Message;
    }

    public int Row { get; }

    public string RowText => $"第 {Row} 行";

    public string Message { get; }
}
