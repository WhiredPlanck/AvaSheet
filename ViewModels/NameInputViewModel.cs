using CommunityToolkit.Mvvm.ComponentModel;

namespace ExamSystem.ViewModels;

/// <summary>通用的单行文本输入弹窗，新建和重命名共用。</summary>
public partial class NameInputViewModel : ViewModelBase
{
    public string Title { get; init; } = "";

    public string Label { get; init; } = "名称";

    public string ConfirmText { get; init; } = "确定";

    public string Placeholder { get; init; } = "给这个题库起个名字";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    public partial string Value { get; set; } = "";

    public bool CanConfirm => !string.IsNullOrWhiteSpace(Value);
}
