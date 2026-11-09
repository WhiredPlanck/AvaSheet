using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AvaSheet.Services;

/// <summary>重启自身，用于让启动期设置（渲染模式）生效。</summary>
public static class AppRestart
{
    /// <summary>
    /// 启动一个新的自身实例。成功返回 true，调用方随后退出当前进程。
    /// 用 UseShellExecute=false 直接执行可执行文件，避免 Linux 上绕道 xdg-open。
    /// </summary>
    public static bool TryStartNewInstance()
    {
        var startInfo = CreateStartInfo();
        if (startInfo is null)
        {
            return false;
        }

        try
        {
            return Process.Start(startInfo) is not null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// 组装重启用的启动信息，无法确定要启动什么时返回 null。
    /// 三个参数默认取当前进程，测试时可以显式传入。
    /// </summary>
    /// <param name="processPath">当前进程的可执行文件；以 <c>dotnet app.dll</c> 启动时这里是 dotnet 宿主。</param>
    /// <param name="entryPath">应用的程序集路径，只有进程是 dotnet 宿主时才需要。</param>
    /// <param name="arguments">要带过去的命令行参数，默认取本进程收到的全部参数。</param>
    public static ProcessStartInfo? CreateStartInfo(
        string? processPath = null,
        string? entryPath = null,
        IReadOnlyList<string>? arguments = null)
    {
        var path = processPath ?? Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };

        // `dotnet app.dll` 启动时 ProcessPath 是宿主，直接执行它只会得到不带参数的 dotnet，
        // 所以要把程序集路径补成第一个参数。
        if (IsDotnetHost(path))
        {
            var entry = entryPath ?? GetEntryPath();
            if (entry is null)
            {
                return null;
            }

            startInfo.ArgumentList.Add(entry);
        }

        // 带上原来的命令行参数，让 --render-diag、--font-diag、--gpu 之类在重启后仍然有效。
        foreach (var argument in arguments ?? Environment.GetCommandLineArgs().Skip(1))
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static bool IsDotnetHost(string path)
        => Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 找出应用的程序集路径，只在以 dotnet 宿主启动时用到。
    /// 不用 Assembly.Location：单文件发布下它恒为空，AOT 分析器会报 IL3000。
    /// </summary>
    private static string? GetEntryPath()
    {
        // `dotnet app.dll` 时命令行第一个参数就是程序集。
        var commandLine = Environment.GetCommandLineArgs();
        if (commandLine.Length > 0
            && commandLine[0].EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            && File.Exists(commandLine[0]))
        {
            return commandLine[0];
        }

        var name = Assembly.GetEntryAssembly()?.GetName().Name;
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var candidate = Path.Combine(AppContext.BaseDirectory, name + ".dll");
        return File.Exists(candidate) ? candidate : null;
    }
}
