namespace ExamSystem.Models;

/// <summary>题目类型。</summary>
public enum QuestionType
{
    /// <summary>单选：只有一个正确答案。</summary>
    SingleChoice,

    /// <summary>多选：两个及以上正确答案。</summary>
    MultipleChoice,

    /// <summary>判断：答案为 T / F。</summary>
    TrueFalse,
}

/// <summary>题型在界面上的中文名与短名。</summary>
public static class QuestionTypeExtensions
{
    public static string ToLabel(this QuestionType type) => type switch
    {
        QuestionType.SingleChoice => "单选题",
        QuestionType.MultipleChoice => "多选题",
        QuestionType.TrueFalse => "判断题",
        _ => "题目",
    };

    public static string ToShortLabel(this QuestionType type) => type switch
    {
        QuestionType.SingleChoice => "单选",
        QuestionType.MultipleChoice => "多选",
        QuestionType.TrueFalse => "判断",
        _ => "题目",
    };
}
