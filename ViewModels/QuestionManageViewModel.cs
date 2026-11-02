using System;

namespace ExamSystem.ViewModels;

/// <summary>题目管理页（阶段 3 实现）。</summary>
public sealed class QuestionManageViewModel : PageViewModelBase
{
    public QuestionManageViewModel()
    {
    }

    public QuestionManageViewModel(Action goBack)
        : base(goBack)
    {
    }

    public override string Title => "管理题目";

    public override string Description => "增删改查、按类型筛选，支持批量导入与导出。";
}
