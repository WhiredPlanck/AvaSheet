namespace ExamSystem.ViewModels;

/// <summary>
/// 交卷后小窗里显示的成绩摘要。
///
/// 只是一份只读快照：关掉小窗后回到卷面逐题回顾，小窗不再需要跟着卷面变化。
/// </summary>
public sealed class ExamResultViewModel(
    string bankName,
    string scoreText,
    string totalText,
    string accuracyText,
    string elapsedText,
    string summary,
    string? unansweredText,
    string? wrongBookNote)
{
    public string BankName { get; } = bankName;

    public string ScoreText { get; } = scoreText;

    /// <summary>形如「/ 19」的满分，跟分数并排显示。</summary>
    public string TotalText { get; } = totalText;

    public string AccuracyText { get; } = accuracyText;

    public string ElapsedText { get; } = elapsedText;

    /// <summary>答对、答错、未答各多少题。</summary>
    public string Summary { get; } = summary;

    public string? UnansweredText { get; } = unansweredText;

    public bool HasUnanswered => !string.IsNullOrEmpty(UnansweredText);

    public string? WrongBookNote { get; } = wrongBookNote;

    public bool HasWrongBookNote => !string.IsNullOrEmpty(WrongBookNote);
}
