using System.Collections.Generic;
using System.Threading.Tasks;
using AvaSheet.Models;

namespace AvaSheet.Services;

/// <summary>
/// 考试记录的读写。记录只增不改，界面只提供删除，
/// 所以接口里没有 Save 之外的更新操作。
/// </summary>
public interface IExamRecordStore
{
    /// <summary>按交卷时间倒序返回全部记录。</summary>
    Task<IReadOnlyList<ExamRecord>> ListAsync();

    /// <summary>写入一条记录（同 Id 覆盖）。</summary>
    Task SaveAsync(ExamRecord record);

    Task DeleteAsync(string id);

    Task ClearAsync();
}
