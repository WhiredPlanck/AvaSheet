using System;
using System.IO;
using System.Linq;

namespace ExamSystem.Services;

/// <summary>
/// 用户数据目录。Windows 是 %AppData%\ExamSystem，Linux 是 ~/.config/ExamSystem，
/// macOS 是 ~/Library/Application Support/ExamSystem。
/// </summary>
public static class AppPaths
{
    /// <summary>数据根目录。</summary>
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ExamSystem");

    /// <summary>题库目录，一库一文件。</summary>
    public static string BanksDirectory { get; } = Path.Combine(RootDirectory, "banks");

    /// <summary>用户数据目录：每库一个进度文件（错题本、练习进度）。</summary>
    public static string ProgressDirectory { get; } = Path.Combine(RootDirectory, "progress");

    /// <summary>偏好设置文件。</summary>
    public static string SettingsFile { get; } = Path.Combine(RootDirectory, "settings.json");

    /// <summary>考试记录文件。</summary>
    public static string RecordsFile { get; } = Path.Combine(RootDirectory, "records.json");

    /// <summary>
    /// 题库 Id 只允许字母、数字、下划线和连字符，避免路径穿越，
    /// 也保证跨平台文件名安全。
    /// </summary>
    public static bool IsValidBankId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64)
        {
            return false;
        }

        return id.All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-');
    }
}
