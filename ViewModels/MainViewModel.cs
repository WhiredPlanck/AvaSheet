using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ExamSystem.ViewModels;

/// <summary>主界面可进入的功能页。</summary>
public enum PageKind
{
    Exam,
    Practice,
    Questions,
}

/// <summary>
/// 主窗口的壳：顶栏（题库选择）、内容区（CurrentPage）、状态栏。
/// 页面一律做成 ViewModelBase，由 ViewLocator 按命名约定找对应 View。
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private ExamViewModel? _exam;
    private PracticeViewModel? _practice;
    private QuestionManageViewModel? _questions;

    public MainViewModel()
    {
        Home = new HomeViewModel(Navigate);
        CurrentPage = Home;
    }

    public HomeViewModel Home { get; }

    /// <summary>题库列表占位，阶段 2 起改为 ObservableCollection&lt;BankSummary&gt;。</summary>
    public ObservableCollection<string> Banks { get; } = [];

    [ObservableProperty]
    public partial ViewModelBase? CurrentPage { get; set; }

    [ObservableProperty]
    public partial string? SelectedBank { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "就绪";

    private void Navigate(PageKind kind)
    {
        CurrentPage = kind switch
        {
            PageKind.Exam => _exam ??= new ExamViewModel(GoHome),
            PageKind.Practice => _practice ??= new PracticeViewModel(GoHome),
            PageKind.Questions => _questions ??= new QuestionManageViewModel(GoHome),
            _ => Home,
        };

        StatusText = CurrentPage is PageViewModelBase page ? page.Title : "就绪";
    }

    [RelayCommand]
    private void GoHome()
    {
        CurrentPage = Home;
        StatusText = "就绪";
    }

    [RelayCommand]
    private void OpenBankManager() => StatusText = "题库管理将在下一阶段开放";

    [RelayCommand]
    private void About() => StatusText = "ExamSystem 0.1 · 单机考试系统";

    [RelayCommand]
    private void Exit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
