using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ExamSystem.ViewModels;

namespace ExamSystem.Views;

public partial class NameInputWindow : Window
{
    public NameInputWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            Confirm();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Confirm();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void Confirm()
    {
        if (DataContext is NameInputViewModel viewModel && viewModel.CanConfirm)
        {
            Close(viewModel.Value.Trim());
        }
    }
}
