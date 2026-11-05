using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamSystem.Models;

namespace ExamSystem.Services;

/// <summary>题库列表项，只含列表要展示的元数据。</summary>
public sealed record BankSummary(
    string Id,
    string Name,
    string? Description,
    int QuestionCount,
    DateTimeOffset UpdatedAt);

/// <summary>
/// 题库存储。ViewModel 只依赖这个接口，不直接碰 File / JsonSerializer，
/// 将来换成 SQLite 只需另写一个实现。
/// </summary>
public interface IBankStore
{
    /// <summary>枚举所有题库的摘要，不返回 questions，首屏用。</summary>
    Task<IReadOnlyList<BankSummary>> ListAsync(CancellationToken ct = default);

    /// <summary>读取完整题库，不存在返回 null。</summary>
    Task<QuestionBank?> LoadAsync(string id, CancellationToken ct = default);

    /// <summary>新建空题库，返回新题库的 Id。</summary>
    Task<string> CreateAsync(string name, string? description = null, CancellationToken ct = default);

    /// <summary>重命名，只改元数据不动 questions。</summary>
    Task RenameAsync(string id, string newName, CancellationToken ct = default);

    /// <summary>删除题库（连同文件）。</summary>
    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>保存完整题库，实现内部保证原子替换。</summary>
    Task SaveAsync(QuestionBank bank, CancellationToken ct = default);

    /// <summary>判断 Id 是否已存在。</summary>
    Task<bool> ExistsAsync(string id, CancellationToken ct = default);

    /// <summary>上次 <see cref="ListAsync"/> 时无法解析的文件名，用于提示用户而不是静默隐藏。</summary>
    IReadOnlyList<string> UnreadableFiles { get; }
}
