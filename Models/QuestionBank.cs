using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ExamSystem.Models;

/// <summary>一个题库，对应磁盘上的一个 JSON 文件。</summary>
public sealed class QuestionBank
{
    /// <summary>
    /// 当前程序能读写的格式版本。v2 起多出 <see cref="WrongQuestionIds"/>，
    /// v3 起多出 <see cref="PracticeProgress"/>，旧文件读进来都是空集合。
    /// </summary>
    public const int CurrentSchemaVersion = 3;

    /// <summary>
    /// 题库 Id 就是文件名，不写进 JSON，避免内容和文件名不一致。
    /// </summary>
    [JsonIgnore]
    public string Id { get; set; } = "";

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<Question> Questions { get; set; } = [];

    /// <summary>
    /// 错题本：做错过的题目 Id。答对后从集合里移除，
    /// 所以这里始终表示"当前还没掌握的题"。
    /// </summary>
    public List<string> WrongQuestionIds { get; set; } = [];

    /// <summary>
    /// 平时练习的进度：每个题型各自记下已经做完的题目 Id。
    /// 做过的题不会再出现在下一轮里，清除进度后重新开始。
    /// </summary>
    public List<PracticeProgressEntry> PracticeProgress { get; set; } = [];
}

/// <summary>一个题型的练习进度。</summary>
public sealed class PracticeProgressEntry
{
    public QuestionType Type { get; set; }

    /// <summary>这个题型里已经做完的题目 Id。</summary>
    public List<string> DoneIds { get; set; } = [];
}
