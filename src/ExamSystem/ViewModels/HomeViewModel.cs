using System;
using CommunityToolkit.Mvvm.Input;

namespace ExamSystem.ViewModels;

/// <summary>首页：三个主入口，加上「管理题目」「考试记录」两个次要入口。</summary>
public partial class HomeViewModel(Action<PageKind>? navigate) : ViewModelBase
{
    private int _wrongBookCount;
    private int _recordCount;
    private double? _lastScore;
    private double? _lastTotal;

    /// <summary>供 XAML 设计器预览使用。</summary>
    public HomeViewModel()
        : this(null)
    {
    }

    public string Headline => "开始今天的练习";

    public string Subheadline => "题目全部来自本地题库，不需要联网。";

    /// <summary>当前题库里还没掌握的题目数，卡片上直接显示。</summary>
    public int WrongBookCount => _wrongBookCount;

    public string WrongBookHint => WrongBookCount > 0
        ? $"还有 {WrongBookCount} 道错题，专门刷这些。"
        : "做错的题会自动收进来。";

    public string RecordsHint => _recordCount == 0
        ? "交卷后自动留档，方便回看。"
        : _lastScore is { } score && _lastTotal is { } total
            ? $"已记录 {_recordCount} 次考试，最近一次 {score:0.##} / {total:0.##} 分。"
            : $"已记录 {_recordCount} 次考试。";

    [RelayCommand]
    private void OpenExam() => navigate?.Invoke(PageKind.Exam);

    [RelayCommand]
    private void OpenPractice() => navigate?.Invoke(PageKind.Practice);

    [RelayCommand]
    private void OpenWrongBook() => navigate?.Invoke(PageKind.WrongBook);

    [RelayCommand]
    private void OpenQuestions() => navigate?.Invoke(PageKind.Questions);

    [RelayCommand]
    private void OpenRecords() => navigate?.Invoke(PageKind.Records);

    /// <summary>题库或错题数变化后刷新卡片上的动态文案。</summary>
    public void RefreshBankInfo(int wrongBookCount)
    {
        _wrongBookCount = wrongBookCount;
        OnPropertyChanged(nameof(WrongBookCount));
        OnPropertyChanged(nameof(WrongBookHint));
    }

    /// <summary>考试记录有增删后刷新卡片上的动态文案。</summary>
    public void RefreshRecordInfo(int count, double? lastScore, double? lastTotal)
    {
        _recordCount = count;
        _lastScore = lastScore;
        _lastTotal = lastTotal;
        OnPropertyChanged(nameof(RecordsHint));
    }
}
