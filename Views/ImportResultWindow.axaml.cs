using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ExamSystem.Views;

public partial class ImportResultWindow : Window
{
    public ImportResultWindow()
    {
        InitializeComponent();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
