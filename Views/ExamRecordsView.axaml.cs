using Avalonia.Controls;

namespace ExamSystem.Views;

public partial class ExamRecordsView : UserControl
{
    public ExamRecordsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.ExamRecordsViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }
}
