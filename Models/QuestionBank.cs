using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ExamSystem.Models;

/// <summary>一个题库，对应磁盘上的一个 JSON 文件。</summary>
/// <remarks>
/// 这里只放题目与元数据。错题本和练习进度是用户数据，存在
/// <see cref="BankProgress"/> 里（progress/{bankId}.json），
/// 这样题库文件可以随便拷贝、导出、覆盖。
/// </remarks>
public sealed class QuestionBank
{
    /// <summary>当前程序写出的格式版本。用户数据（错题本、练习进度）不在题库文件里。</summary>
    public const int CurrentSchemaVersion = 4;

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
}
