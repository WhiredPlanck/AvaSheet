using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ExamSystem.ViewModels;

namespace ExamSystem.Views;

public partial class BankManagerWindow : Window
{
    public BankManagerWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is BankManagerViewModel viewModel)
            {
                await viewModel.InitializeAsync();
            }
        };
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close(true);
}
