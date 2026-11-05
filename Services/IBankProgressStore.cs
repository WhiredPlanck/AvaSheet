using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamSystem.Models;

namespace ExamSystem.Services;

/// <summary>题库的用户数据（错题本、练习进度）。</summary>
public interface IBankProgressStore
{
    /// <summary>读取某个题库的进度。文件不存在或读不出来时返回空进度。</summary>
    Task<BankProgress> LoadAsync(string bankId, CancellationToken ct = default);

    /// <summary>保存进度。没有任何内容时删除文件，不留空壳。</summary>
    Task SaveAsync(string bankId, BankProgress progress, CancellationToken ct = default);

    /// <summary>删除某个题库的进度，题库被删时一并清掉。</summary>
    Task DeleteAsync(string bankId, CancellationToken ct = default);
}
