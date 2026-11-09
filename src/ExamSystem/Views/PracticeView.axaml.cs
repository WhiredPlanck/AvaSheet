using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ExamSystem.ViewModels;

namespace ExamSystem.Views;

public partial class PracticeView : UserControl
{
    private PracticeViewModel? _viewModel;

    public PracticeView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Attach(DataContext as PracticeViewModel);

        if (DataContext is PracticeViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e) => Attach(null);

    private void OnDataContextChanged(object? sender, System.EventArgs e) =>
        Attach(DataContext as PracticeViewModel);

    private void Attach(PracticeViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    // 判定后把结果条滚进视野：题目选项多的时候，结果和解析在卡片底部之外。
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PracticeViewModel.HasResult) || _viewModel?.HasResult != true)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => ResultBanner.BringIntoView(), DispatcherPriority.Background);
    }
}
