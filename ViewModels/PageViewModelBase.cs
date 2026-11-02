using System;
using CommunityToolkit.Mvvm.Input;

namespace ExamSystem.ViewModels;

/// <summary>功能页基类：统一提供「返回首页」。</summary>
public abstract class PageViewModelBase : ViewModelBase
{
    protected PageViewModelBase(Action? goBack = null)
    {
        GoBackCommand = new RelayCommand(() => goBack?.Invoke());
    }

    public abstract string Title { get; }

    public abstract string Description { get; }

    public IRelayCommand GoBackCommand { get; }
}
