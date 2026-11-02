using System;
using CommunityToolkit.Mvvm.Input;

namespace ExamSystem.ViewModels;

/// <summary>首页：三张入口卡片。</summary>
public partial class HomeViewModel : ViewModelBase
{
    private readonly Action<PageKind>? _navigate;

    /// <summary>供 XAML 设计器预览使用。</summary>
    public HomeViewModel()
        : this(null)
    {
    }

    public HomeViewModel(Action<PageKind>? navigate)
    {
        _navigate = navigate;
    }

    public string Headline => "开始今天的练习";

    public string Subheadline => "选择一个入口，题目全部来自本地题库，不需要联网。";

    [RelayCommand]
    private void OpenExam() => _navigate?.Invoke(PageKind.Exam);

    [RelayCommand]
    private void OpenPractice() => _navigate?.Invoke(PageKind.Practice);

    [RelayCommand]
    private void OpenQuestions() => _navigate?.Invoke(PageKind.Questions);
}
