using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json.Serialization;

namespace ExamSystem.Models;

/// <summary>
/// 一个题库的用户数据：错题本与练习进度。
///
/// 这些数据刻意不写进题库文件：题库是可以随便拷贝、导出、覆盖的内容，
/// 而错题本和练习进度是「这台机器上这个人」的状态。分开存之后，
/// 导出的题库天然干净，导入别人的题库也不会带上自己的进度。
/// 对应磁盘上的 progress/{bankId}.json。
/// </summary>
public sealed class BankProgress
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// 错题本：做错过的题目 Id。答对后从集合里移除，
    /// 所以这里始终表示"当前还没掌握的题"。
    /// </summary>
    [AllowNull]
    public List<string> WrongQuestionIds { get; set => field = value ?? []; } = [];

    /// <summary>
    /// 平时练习的进度：每个题型各自记下已经做完的题目 Id。
    /// 做过的题不会再出现在下一轮里，清除进度后重新开始。
    /// </summary>
    [AllowNull]
    public List<PracticeProgressEntry> PracticeProgress { get; set => field = value ?? []; } = [];

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>没有任何进度时不写文件，避免留下空壳。</summary>
    [JsonIgnore]
    public bool IsEmpty
        => WrongQuestionIds.Count == 0
            && PracticeProgress.All(entry => entry.DoneIds.Count == 0);
}

/// <summary>一个题型的练习进度。</summary>
public sealed class PracticeProgressEntry
{
    public QuestionType Type { get; set; }

    /// <summary>这个题型里已经做完的题目 Id。JSON 里的 null 按空列表处理。</summary>
    [AllowNull]
    public List<string> DoneIds { get; set => field = value ?? []; } = [];
}
