using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaSheet.Services;

namespace AvaSheet.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        Title = "关于 " + AppInfo.Name;
        VersionLabel.Text = AppInfo.VersionText;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
