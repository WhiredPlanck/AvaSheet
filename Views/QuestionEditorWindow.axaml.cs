using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ExamSystem.Models;
using ExamSystem.ViewModels;

namespace ExamSystem.Views;

public partial class QuestionEditorWindow : Window
{
    public QuestionEditorWindow()
    {
        InitializeComponent();
    }

    /// <summary>保存成功后的题目；取消或校验失败时为 null。</summary>
    public Question? Result { get; private set; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(false);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
        {
            Save();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnSave(object? sender, RoutedEventArgs e) => Save();

    private void Save()
    {
        if (DataContext is not QuestionEditorViewModel viewModel)
        {
            return;
        }

        if (!viewModel.TryBuild(out var question, out var error))
        {
            viewModel.ErrorMessage = error;
            return;
        }

        Result = question;
        Close(true);
    }
}
