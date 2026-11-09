using System.Collections.Generic;
using System.Linq;

namespace AvaSheet.Models;

/// <summary>
/// 答案键在界面上的写法：判断题显示「正确 / 错误」，选择题原样显示字母。
/// 练习、考试、考试记录三处都用它，保证同一个答案在不同页面长得一样。
/// </summary>
public static class AnswerText
{
    public static string Key(string key) => key switch
    {
        "T" => "正确",
        "F" => "错误",
        _ => key,
    };

    /// <summary>多个答案用顿号连接，例如「A、C」。</summary>
    public static string Join(IEnumerable<string> keys) => string.Join("、", keys.Select(Key));
}
