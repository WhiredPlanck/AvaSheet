using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaSheet.ViewModels;

namespace AvaSheet.Views;

public partial class QuestionManageView : UserControl
{
    public QuestionManageView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is QuestionManageViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        // 双击行内任意位置都进入编辑，不用先精确点在文字上。
        if (e.Source is not Visual source)
        {
            return;
        }

        var row = source.FindAncestorOfType<ListBoxItem>()?.DataContext as QuestionRowViewModel;
        if (row?.EditCommand.CanExecute(null) == true)
        {
            row.EditCommand.Execute(null);
        }
    }
}
