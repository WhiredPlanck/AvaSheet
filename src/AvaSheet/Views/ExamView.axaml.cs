using Avalonia.Controls;
using AvaSheet.ViewModels;

namespace AvaSheet.Views;

public partial class ExamView : UserControl
{
    public ExamView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ExamViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }
}
