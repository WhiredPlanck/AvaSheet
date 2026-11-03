using System;
using System.Diagnostics;
using System.IO;

namespace ExamSystem.Services;

/// <summary>重启自身，用于让启动期设置（渲染模式）生效。</summary>
public static class AppRestart
{
    /// <summary>
    /// 启动一个新的自身实例。成功返回 true，调用方随后退出当前进程。
    /// 用 UseShellExecute=false 直接执行可执行文件，避免 Linux 上绕道 xdg-open。
    /// </summary>
    public static bool TryStartNewInstance()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory,
            };

            return Process.Start(startInfo) is not null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }
}
