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
