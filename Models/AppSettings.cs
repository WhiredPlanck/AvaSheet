namespace ExamSystem.Models;

/// <summary>跨题库的用户偏好，存在 settings.json。</summary>
public sealed class AppSettings
{
    /// <summary>上次选中的题库 Id。</summary>
    public string? LastBankId { get; set; }

    /// <summary>主窗口是否最大化。</summary>
    public bool IsWindowMaximized { get; set; }
}
