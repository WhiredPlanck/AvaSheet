using System;

namespace ExamSystem.ViewModels;

/// <summary>模拟考试页（阶段 5 实现）。</summary>
public sealed class ExamViewModel : PageViewModelBase
{
    public ExamViewModel()
    {
    }

    public ExamViewModel(Action goBack)
        : base(goBack)
    {
    }

    public override string Title => "模拟考试";

    public override string Description => "按题型组卷、限时作答，交卷后统一评分并给出成绩。";
}
