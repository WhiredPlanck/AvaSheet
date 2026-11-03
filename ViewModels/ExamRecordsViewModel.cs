using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExamSystem.Models;
using ExamSystem.Services;

namespace ExamSystem.ViewModels;

/// <summary>考试记录页当前处于哪个阶段。</summary>
public enum ExamRecordsPhase
{
    Loading,

    /// <summary>一条记录都没有。</summary>
    Empty,

    /// <summary>有记录可看。</summary>
    Ready,
}

/// <summary>
/// 考试记录：每次交卷自动留档，这里按时间倒序回看成绩与逐题作答。
///
/// 记录里存的是题目快照，所以题库改了、删了也能照常回看。
/// </summary>
public sealed partial class ExamRecordsViewModel : PageViewModelBase
{
    private readonly IExamRecordStore? _store;
    private readonly IDialogService? _dialogs;
    private readonly Action? _openExam;

    private bool _initialized;

    /// <summary>供 XAML 预览器使用。</summary>
    public ExamRecordsViewModel()
        : this(null, null)
    {
    }

    public ExamRecordsViewModel(
        IExamRecordStore? store,
        IDialogService? dialogs,
        Action? goBack = null,
        Action? openExam = null)
        : base(goBack)
    {
        _store = store;
        _dialogs = dialogs;
        _openExam = openExam;
    }

    public override string Title => "考试记录";

    public override string Description => "历次考试的成绩与逐题作答。";

    [ObservableProperty]
    public partial ExamRecordsPhase Phase { get; set; } = ExamRecordsPhase.Loading;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool IsLoading => Phase == ExamRecordsPhase.Loading;

    public bool IsEmpty => Phase == ExamRecordsPhase.Empty;

    public bool HasRecords => Phase == ExamRecordsPhase.Ready;

    public ObservableCollection<ExamRecordRowViewModel> Records { get; } = [];

    // ---------------------------------------------------------------- 汇总

    private double TotalQuestions => Records.Sum(r => r.Record.Total);

    private double TotalScore => Records.Sum(r => r.Record.Score);

    public string CountText => $"{Records.Count} 次";

    public string AverageText => TotalQuestions == 0
        ? "0%"
        : $"{Math.Round(100.0 * TotalScore / TotalQuestions)}%";

    public string RecentText => Records.Count == 0
        ? "—"
        : $"{FormatScore(Records[0].Record.Score)} / {FormatScore(Records[0].Record.Total)}";

    internal static string FormatScore(double value) => value.ToString("0.##");

    // ---------------------------------------------------------------- 加载 / 删除

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        if (_store is null)
        {
            Phase = ExamRecordsPhase.Empty;
            return;
        }

        try
        {
            var records = await _store.ListAsync();

            Records.Clear();
            foreach (var record in records)
            {
                Records.Add(new ExamRecordRowViewModel(record, DeleteRowAsync));
            }

            Phase = Records.Count == 0 ? ExamRecordsPhase.Empty : ExamRecordsPhase.Ready;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            Phase = Records.Count == 0 ? ExamRecordsPhase.Empty : ExamRecordsPhase.Ready;
            ErrorMessage = Describe(ex);
        }

        NotifyStats();
    }

    private async Task DeleteRowAsync(ExamRecordRowViewModel row)
    {
        if (_store is null)
        {
            return;
        }

        if (_dialogs is not null &&
            !await _dialogs.ConfirmAsync(
                "删除考试记录",
                $"确定删除「{row.BankName}」{row.DateText} 的这次成绩吗？删除后不能恢复。",
                "删除",
                destructive: true))
        {
            return;
        }

        try
        {
            await _store.DeleteAsync(row.Record.Id);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = Describe(ex);
            OnPropertyChanged(nameof(HasError));
        }
    }

    [RelayCommand]
    private async Task ClearAllAsync()
    {
        if (_store is null || Records.Count == 0)
        {
            return;
        }

        if (_dialogs is not null &&
            !await _dialogs.ConfirmAsync(
                "清空考试记录",
                $"共 {Records.Count} 条记录会被永久删除，确定吗？",
                "全部删除",
                destructive: true))
        {
            return;
        }

        try
        {
            await _store.ClearAsync();
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = Describe(ex);
            OnPropertyChanged(nameof(HasError));
        }
    }

    [RelayCommand]
    private void OpenExam() => _openExam?.Invoke();

    private void NotifyStats()
    {        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasRecords));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(AverageText));
        OnPropertyChanged(nameof(RecentText));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Title));
    }

    private static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "没有权限读写考试记录，检查一下数据目录的访问权限。",
        System.IO.IOException => $"读写考试记录失败：{ex.Message}",
        _ => $"出现意外错误：{ex.Message}",
    };
}

/// <summary>记录列表里的一条：摘要 + 可展开的逐题作答。</summary>
public sealed partial class ExamRecordRowViewModel : ObservableObject
{
    private readonly Func<ExamRecordRowViewModel, Task>? _delete;

    public ExamRecordRowViewModel(ExamRecord record, Func<ExamRecordRowViewModel, Task>? delete = null)
    {
        Record = record;
        _delete = delete;
        Answers = [.. record.Answers.Select((answer, index) => new ExamRecordAnswerViewModel(answer, index + 1))];
    }

    public ExamRecord Record { get; }

    public IReadOnlyList<ExamRecordAnswerViewModel> Answers { get; }

    public string BankName => string.IsNullOrWhiteSpace(Record.BankName) ? "未知题库" : Record.BankName;

    public string DateText => Record.FinishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string ScoreText => Record.Score.ToString("0.##");

    public string ScoreTotalText => $"/ {Record.Total.ToString("0.##")}";

    public string AccuracyText => Record.Total <= 0
        ? "0%"
        : $"{Math.Round(100.0 * Record.Score / Record.Total)}%";

    public string DurationText => Record.ElapsedSeconds >= 60
        ? $"{Record.ElapsedSeconds / 60} 分 {Record.ElapsedSeconds % 60} 秒"
        : $"{Record.ElapsedSeconds} 秒";

    /// <summary>记录里的题数。旧记录没存逐题明细时用各项计数推。</summary>
    public int QuestionCount => Record.Answers.Count > 0
        ? Record.Answers.Count
        : Record.CorrectCount + Record.WrongCount + Record.UnansweredCount;

    public string MetaText =>
        $"{QuestionCount} 题 · 限时 {Record.LimitMinutes} 分钟 · {DurationText}";

    /// <summary>「时间到自动交卷」「3 题未答」这类需要提醒的补充说明。</summary>
    public string NoteText
    {
        get
        {
            var notes = new List<string>();

            if (Record.AutoSubmitted)
            {
                notes.Add("时间到自动交卷");
            }

            if (Record.UnansweredCount > 0)
            {
                notes.Add($"{Record.UnansweredCount} 题未答");
            }

            return string.Join(" · ", notes);
        }
    }

    public bool HasNote => NoteText.Length > 0;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public string ExpandText => IsExpanded ? "收起" : "展开";

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private async Task Delete()
    {
        if (_delete is not null)
        {
            await _delete(this);
        }
    }

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandText));
}

/// <summary>记录详情里的一道题。</summary>
public sealed class ExamRecordAnswerViewModel
{
    public ExamRecordAnswerViewModel(ExamRecordAnswer answer, int number)
    {
        Answer = answer;
        Number = number;
    }

    public ExamRecordAnswer Answer { get; }

    public int Number { get; }

    public string TypeShort => Answer.Type.ToShortLabel();

    /// <summary>这道题的分值，0 表示旧记录里没存。</summary>
    public string ScoreText => Answer.Score > 0 ? $"{Answer.Score:0.##} 分" : "";

    public bool HasScore => Answer.Score > 0;

    public string Stem => Answer.Stem;

    public bool IsAnswered => Answer.IsAnswered;

    public bool IsCorrect => Answer.IsCorrect;

    public bool IsWrong => Answer.IsAnswered && !Answer.IsCorrect;

    public bool IsSkipped => !Answer.IsAnswered;

    public string ResultText => !Answer.IsAnswered ? "未答" : Answer.IsCorrect ? "答对" : "答错";

    public string YourAnswerText => Answer.IsAnswered
        ? "你的答案：" + string.Join("、", Answer.Selected.Select(DisplayKey))
        : "未作答";

    public string CorrectAnswerText => "正确答案：" + string.Join("、", Answer.Correct.Select(DisplayKey));

    public bool HasExplanation => !string.IsNullOrWhiteSpace(Answer.Explanation);

    public string ExplanationText => string.IsNullOrWhiteSpace(Answer.Explanation)
        ? ""
        : "解析：" + Answer.Explanation;

    /// <summary>判断题对用户显示「正确 / 错误」，而不是 T / F。</summary>
    private static string DisplayKey(string key) => key switch
    {
        "T" => "正确",
        "F" => "错误",
        _ => key,
    };
}
