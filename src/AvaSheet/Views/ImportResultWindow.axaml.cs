using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AvaSheet.Views;

public partial class ImportResultWindow : Window
{
    public ImportResultWindow()
    {
        InitializeComponent();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
