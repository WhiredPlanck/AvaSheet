using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace ExamSystem.Models;

/// <summary>一道题目。判断题的 <see cref="Choices"/> 为空，<see cref="Answers"/> 存 "T" 或 "F"。</summary>
public sealed class Question
{
    /// <summary>库内唯一 Id，导入时若缺失会自动生成。</summary>
    [AllowNull]
    public string Id { get; set => field = value ?? ""; } = "";

    public QuestionType Type { get; set; }

    /// <summary>题干，纯文本。</summary>
    [AllowNull]
    public string Stem { get; set => field = value ?? ""; } = "";

    /// <summary>选项，按展示顺序排列。JSON 里的 null 按空列表处理。</summary>
    [AllowNull]
    public List<QuestionChoice> Choices { get; set => field = value ?? []; } = [];

    /// <summary>正确答案的选项 Key；判断题用 "T"/"F"。JSON 里的 null 按空列表处理。</summary>
    [AllowNull]
    public List<string> Answers { get; set => field = value ?? []; } = [];

    /// <summary>解析，可为空。</summary>
    public string? Explanation { get; set; }

    /// <summary>难度 1–5，1 最简单。</summary>
    public int Difficulty { get; set; } = 1;

    /// <summary>标签。JSON 里的 null 按空列表处理。</summary>
    [AllowNull]
    public List<string> Tags { get; set => field = value ?? []; } = [];
}

/// <summary>一个选项。</summary>
public sealed class QuestionChoice
{
    /// <summary>选项标识，通常是 "A"、"B"…</summary>
    [AllowNull]
    public string Key { get; set => field = value ?? ""; } = "";

    /// <summary>选项文本。</summary>
    [AllowNull]
    public string Text { get; set => field = value ?? ""; } = "";
}
