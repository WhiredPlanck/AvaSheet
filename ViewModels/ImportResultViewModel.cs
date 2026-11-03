using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ExamSystem.Services;

namespace ExamSystem.ViewModels;

/// <summary>导入结束后的小窗内容：导入了多少，哪些行没进来、为什么。</summary>
public sealed class ImportResultViewModel : ViewModelBase
{
    public ImportResultViewModel(int importedCount, IReadOnlyList<ExcelRowError> errors)
    {
        Errors = new ObservableCollection<ImportErrorRowViewModel>(errors.Select(e => new ImportErrorRowViewModel(e)));

        var skipped = Errors.Count;
        SummaryText = (importedCount, skipped) switch
        {
            ( > 0, 0) => $"已导入 {importedCount} 道题。",
            ( > 0, _) => $"已导入 {importedCount} 道题，跳过 {skipped} 行。",
            (0, 0) => "表格里没有题目。",
            _ => $"没有导入任何题目，{skipped} 行都有问题。",
        };
    }

    public string Title => "导入结果";

    public string SummaryText { get; }

    public ObservableCollection<ImportErrorRowViewModel> Errors { get; }

    public bool HasErrors => Errors.Count > 0;

    public string HintText => HasErrors ? "改好表格后可以重新导入，已经导入的题目不会重复。" : "";
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
