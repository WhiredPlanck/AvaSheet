using System.Linq;

namespace AvaSheet.Models;

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

    /// <summary>
    /// 从表格里的写法认出题型，允许中英文与简写。
    /// </summary>
    public static bool TryParseLabel(string? text, out QuestionType type)
    {
        type = QuestionType.SingleChoice;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = new string(text.Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToLowerInvariant();

        switch (value)
        {
            case "单选题" or "单选" or "单项选择" or "单项选择题" or "singlechoice" or "single":
                type = QuestionType.SingleChoice;
                return true;
            case "多选题" or "多选" or "多项选择" or "多项选择题" or "multiplechoice" or "multiple":
                type = QuestionType.MultipleChoice;
                return true;
            case "判断题" or "判断" or "是非题" or "truefalse" or "t" or "f":
                type = QuestionType.TrueFalse;
                return true;
            default:
                return false;
        }
    }
}
