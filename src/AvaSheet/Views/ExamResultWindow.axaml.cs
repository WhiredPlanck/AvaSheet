using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AvaSheet.Views;

/// <summary>交卷后弹出的成绩小窗，只有一个「查看逐题回顾」按钮。</summary>
public partial class ExamResultWindow : Window
{
    public ExamResultWindow()
    {
        InitializeComponent();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
