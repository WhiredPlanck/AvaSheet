namespace AvaSheet.Services;

/// <summary>软件名称与版本号。版本号来自项目文件里的 &lt;Version&gt;，不在代码里重复写。</summary>
public static class AppInfo
{
    /// <summary>界面上显示的名字。程序名、进程名用项目名 AvaSheet。</summary>
    public const string Name = "答卷";

    /// <summary>三段式版本号，例如 1.0.0。</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>关于窗口里显示的版本文案。</summary>
    public static string VersionText => $"版本 {Version}";

    private static string ReadVersion()
    {
        var version = typeof(AppInfo).Assembly.GetName().Version;

        // 没配置 <Version> 时 Build 会是 -1，这时只显示主次版本。
        return version switch
        {
            null => "1.0.0",
            { Build: >= 0 } => version.ToString(3),
            _ => version.ToString(2),
        };
    }
}
