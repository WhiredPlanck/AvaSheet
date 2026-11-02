using System.Collections.Generic;

namespace ExamSystem.Models;

/// <summary>一道题目。判断题的 <see cref="Choices"/> 为空，<see cref="Answers"/> 存 "T" 或 "F"。</summary>
public sealed class Question
{
    /// <summary>库内唯一 Id，导入时若缺失会自动生成。</summary>
    public string Id { get; set; } = "";

    public QuestionType Type { get; set; }

    /// <summary>题干，纯文本。</summary>
    public string Stem { get; set; } = "";

    /// <summary>选项，按展示顺序排列。</summary>
    public List<QuestionChoice> Choices { get; set; } = [];

    /// <summary>正确答案的选项 Key；判断题用 "T"/"F"。</summary>
    public List<string> Answers { get; set; } = [];

    /// <summary>解析，可为空。</summary>
    public string? Explanation { get; set; }

    /// <summary>难度 1–5，1 最简单。</summary>
    public int Difficulty { get; set; } = 1;

    public List<string> Tags { get; set; } = [];
}

/// <summary>一个选项。</summary>
public sealed class QuestionChoice
{
    /// <summary>选项标识，通常是 "A"、"B"…</summary>
    public string Key { get; set; } = "";

    /// <summary>选项文本。</summary>
    public string Text { get; set; } = "";
}
