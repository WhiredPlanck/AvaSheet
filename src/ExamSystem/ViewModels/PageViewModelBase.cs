using System;
using CommunityToolkit.Mvvm.Input;

namespace ExamSystem.ViewModels;

/// <summary>
/// 正在答题的页面实现它，主窗口据此隐藏顶栏和状态栏，把空间让给题目。
/// 只有「作答中」才算沉浸，组卷设置、成绩、空状态仍然保留外壳。
/// </summary>
public interface IAnsweringSession
{
    bool IsAnswering { get; }
}

/// <summary>功能页基类：统一提供「返回首页」。</summary>
public abstract class PageViewModelBase(Action? goBack = null) : ViewModelBase
{
    public abstract string Title { get; }

    public abstract string Description { get; }

    public IRelayCommand GoBackCommand { get; } = new RelayCommand(() => goBack?.Invoke());
}
