using System;

namespace ExamSystem.ViewModels;

/// <summary>平时练习页（阶段 4 实现）。</summary>
public sealed class PracticeViewModel : PageViewModelBase
{
    public PracticeViewModel()
    {
    }

    public PracticeViewModel(Action goBack)
        : base(goBack)
    {
    }

    public override string Title => "平时练习";

    public override string Description => "逐题作答、即时判定，答完可以展开解析，并记录错题。";
}
