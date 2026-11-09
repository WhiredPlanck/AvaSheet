using System;
using Avalonia.Controls;
using ExamSystem.ViewModels;

namespace ExamSystem.Views;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();

        Opened += OnOpened;
        Closing += OnClosing;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        await viewModel.InitializeAsync();

        if (viewModel.StartMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        // 考试进行中关窗口，和点「返回首页」一样要先确认，否则一路捷径绕过作废提示。
        if (!_closeConfirmed && viewModel.CurrentPage is ExamViewModel exam && !exam.CanExitWithoutPrompt)
        {
            e.Cancel = true;

            if (await exam.ConfirmExitAsync())
            {
                _closeConfirmed = true;
                Close();
            }

            return;
        }

        _ = viewModel.SaveWindowStateAsync(WindowState == WindowState.Maximized);
    }
}
