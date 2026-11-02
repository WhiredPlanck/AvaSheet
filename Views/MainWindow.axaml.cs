using System;
using Avalonia.Controls;
using ExamSystem.ViewModels;

namespace ExamSystem.Views;

public partial class MainWindow : Window
{
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

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            _ = viewModel.SaveWindowStateAsync(WindowState == WindowState.Maximized);
        }
    }
}
