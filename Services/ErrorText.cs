using System;
using System.IO;

namespace ExamSystem.Services;

/// <summary>
/// 把存储层抛出的异常翻成用户看得懂的提示。
///
/// 各页面共用一套措辞：同一个「没有权限」或「文件坏了」不该在不同页面
/// 显示成不同的句子。<paramref name="subject"/> 说明读写的是什么，
/// 例如「题库」「考试记录」。
/// </summary>
internal static class ErrorText
{
    public static string Describe(Exception ex, string subject = "数据") => ex switch
    {
        InvalidDataException => ex.Message,
        FileNotFoundException => $"{subject}文件不存在，可能已被移动或删除。",
        UnauthorizedAccessException => $"没有权限读写{subject}，请检查数据目录的访问权限。",
        IOException => $"读写{subject}失败：{ex.Message}",
        _ => ex.Message,
    };
}
