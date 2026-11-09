using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AvaSheet.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
    }

    public ConfirmWindow(string title, string message, string confirmText, bool destructive = false)
        : this()
    {
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;

        if (destructive)
        {
            ConfirmButton.Classes.Remove("accent");
            ConfirmButton.Classes.Add("danger");
        }
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
