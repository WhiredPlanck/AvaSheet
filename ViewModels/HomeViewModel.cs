using System;
using CommunityToolkit.Mvvm.Input;
using ExamSystem.Services;

namespace ExamSystem.ViewModels;

/// <summary>首页：四张入口卡片。</summary>
public partial class HomeViewModel : ViewModelBase
{
    private readonly Action<PageKind>? _navigate;
    private readonly Func<BankSummary?>? _getBank;

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

    [RelayCommand]
    private void OpenExam() => _navigate?.Invoke(PageKind.Exam);

    [RelayCommand]
    private void OpenPractice() => _navigate?.Invoke(PageKind.Practice);

    [RelayCommand]
    private void OpenWrongBook() => _navigate?.Invoke(PageKind.WrongBook);

    [RelayCommand]
    private void OpenQuestions() => _navigate?.Invoke(PageKind.Questions);

    /// <summary>题库或错题数变化后刷新卡片上的动态文案。</summary>
    public void RefreshBankInfo()
    {
        OnPropertyChanged(nameof(WrongBookCount));
        OnPropertyChanged(nameof(WrongBookHint));
    }
}
