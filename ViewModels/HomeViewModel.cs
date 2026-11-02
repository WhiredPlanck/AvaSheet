using System;
using CommunityToolkit.Mvvm.Input;
using ExamSystem.Services;

namespace ExamSystem.ViewModels;

/// <summary>首页：三个主入口，加上「管理题目」「考试记录」两个次要入口。</summary>
public partial class HomeViewModel : ViewModelBase
{
    private readonly Action<PageKind>? _navigate;
    private readonly Func<BankSummary?>? _getBank;

    private int _recordCount;
    private int? _lastScore;
    private int? _lastTotal;

    /// <summary>供 XAML 设计器预览使用。</summary>
    public HomeViewModel()
        : this(null, null)
    {
    }

    public HomeViewModel(Action<PageKind>? navigate, Func<BankSummary?>? getBank = null)
    {
        _navigate = navigate;
        _getBank = getBank;
    }

    public string Headline => "开始今天的练习";

    public string Subheadline => "选择一个入口，题目全部来自本地题库，不需要联网。";

    /// <summary>当前题库里还没掌握的题目数，卡片上直接显示。</summary>
    public int WrongBookCount => _getBank?.Invoke()?.WrongCount ?? 0;

    public string WrongBookHint => WrongBookCount > 0
        ? $"还有 {WrongBookCount} 道错题，专门刷这些。"
        : "做错的题会自动收进来。";

    public string RecordsHint => _recordCount == 0
        ? "交卷后自动留档，方便回看。"
        : _lastScore is { } score && _lastTotal is { } total
            ? $"已记录 {_recordCount} 次考试，最近一次 {score} / {total} 分。"
            : $"已记录 {_recordCount} 次考试。";

    [RelayCommand]
    private void OpenExam() => _navigate?.Invoke(PageKind.Exam);

    [RelayCommand]
    private void OpenPractice() => _navigate?.Invoke(PageKind.Practice);

    [RelayCommand]
    private void OpenWrongBook() => _navigate?.Invoke(PageKind.WrongBook);

    [RelayCommand]
    private void OpenQuestions() => _navigate?.Invoke(PageKind.Questions);

    [RelayCommand]
    private void OpenRecords() => _navigate?.Invoke(PageKind.Records);

    /// <summary>题库或错题数变化后刷新卡片上的动态文案。</summary>
    public void RefreshBankInfo()
    {
        OnPropertyChanged(nameof(WrongBookCount));
        OnPropertyChanged(nameof(WrongBookHint));
    }

    /// <summary>考试记录有增删后刷新卡片上的动态文案。</summary>
    public void RefreshRecordInfo(int count, int? lastScore, int? lastTotal)
    {
        _recordCount = count;
        _lastScore = lastScore;
        _lastTotal = lastTotal;
        OnPropertyChanged(nameof(RecordsHint));
    }
}
