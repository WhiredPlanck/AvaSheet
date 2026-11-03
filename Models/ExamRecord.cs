using System;
using System.Collections.Generic;

namespace ExamSystem.Models;

/// <summary>
/// 一次模拟考试的成绩单。交卷时写入，之后只读。
///
/// 题目内容存的是快照：题库后来改了、甚至整个库被删掉，
/// 记录里仍然能回看出当时考了什么、选了什么、正确答案是什么。
/// </summary>
public sealed class ExamRecord
{
    public string Id { get; set; } = "";

    /// <summary>考的是哪个库。库被删掉后这个 Id 就找不到了，靠 BankName 显示。</summary>
    public string? BankId { get; set; }

    public string BankName { get; set; } = "";

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset FinishedAt { get; set; }

    /// <summary>实际用时（秒）。</summary>
    public int ElapsedSeconds { get; set; }

    /// <summary>交卷时用户设置的限时（分钟）。</summary>
    public int LimitMinutes { get; set; }

    /// <summary>是不是时间到被自动交卷的。</summary>
    public bool AutoSubmitted { get; set; }

    /// <summary>得分（可以带 0.5）。</summary>
    public double Score { get; set; }

    /// <summary>满分。</summary>
    public double Total { get; set; }

    public int CorrectCount { get; set; }

    public int WrongCount { get; set; }

    public int UnansweredCount { get; set; }

    public List<ExamRecordAnswer> Answers { get; set; } = [];
}

/// <summary>成绩单里一道题的作答记录。</summary>
public sealed class ExamRecordAnswer
{
    public string QuestionId { get; set; } = "";

    public QuestionType Type { get; set; }

    public string Stem { get; set; } = "";

    /// <summary>用户选的选项键，判断题是 T / F。</summary>
    public List<string> Selected { get; set; } = [];

    /// <summary>正确答案的选项键。</summary>
    public List<string> Correct { get; set; } = [];

    public bool IsAnswered { get; set; }

    public bool IsCorrect { get; set; }

    /// <summary>这道题的分值。</summary>
    public double Score { get; set; }

    public string? Explanation { get; set; }
}
