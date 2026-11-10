# AvaSheet（答卷）

本地的题库与考试练习工具，基于 Avalonia 12 与 .NET 10，界面用 Fluent 主题。

![AvaSheet 主界面](docs/screenshot.png)

## 功能

- **管理题目**：增删改题目，支持从 JSON 与 Excel（`.xlsx` / `.xls` / `.xlsb`）批量导入，导入前自动判重。
- **平时练习**：按题型分组练习，选完立即判定并显示解析，进度自动记住，做错的题收进错题本。
- **错题本**：只刷做错的题，答对后自动移出。
- **模拟考试**：按题型组卷，限时作答，交卷后弹成绩小窗，关掉后回到冻结的卷面逐题回顾。
- **考试记录**：历次成绩落盘，可展开看当时的逐题作答（题目内容是快照，题库改动后仍可回看）。
- 跟随系统的深浅主题、可切换的硬件加速开关。

所有数据都存在用户目录下，不需要联网：

- Windows：`%AppData%\AvaSheet`
- Linux：`~/.config/AvaSheet`

## 目录结构

```
AvaSheet.slnx              解决方案
src/AvaSheet/              主程序
  Models/                    数据模型
  Services/                  存储、导入导出、渲染与主题等基础设施
  ViewModels/                页面与列表项的视图模型
  Views/                     窗口与页面
  Styles/                    颜色令牌、控件主题与页面样式
tests/AvaSheet.Tests/      xunit 测试
tools/make-icon/           应用图标生成脚本
packaging/linux/           Linux 打包脚本与 AppImage 用的桌面项
.github/workflows/         持续集成与发版
docs/                      截图等文档用图
```

## 开发

需要 .NET 10 SDK。

```bash
dotnet build                        # 构建整个解决方案
dotnet test                         # 跑全部测试
dotnet run --project src/AvaSheet         # 启动程序
dotnet publish src/AvaSheet -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

发布产物是单文件自包含的，目标机器不需要装 .NET 运行时。

`App.axaml` 等资源以 `src/AvaSheet` 为根，测试项目通过 `ProjectReference` 引用主项目。

## 启动参数

| 参数 | 作用 |
|---|---|
| `--gpu` | 本次启动强制使用硬件加速（不改设置文件） |
| `--software` | 本次启动强制使用软件渲染（不改设置文件） |
| `--render-diag` | 启动后打印实际使用的渲染后端 |
| `--font-diag` | 启动后打印中文回退字体的命中情况 |

默认使用软件渲染：有的显卡能初始化硬件加速，但运行一段时间后卡死，这种情况 Avalonia 无法自动回退。需要时可以在顶栏 `⋯` 菜单里打开硬件加速，重启后生效。

## 发版

推送到 `main` 与提交 PR 时，`.github/workflows/ci.yml` 会构建（警告视为错误）、跑测试、检查代码格式。

打形如 `v1.0.0` 的标签会触发 `.github/workflows/release.yml`：先用同样的步骤验证一遍，再打 x64 与 arm64 两个架构，每个架构产出一个 AppImage 和一个 tar 包，连同 `checksums.txt` 一起挂到 GitHub Release 上。产物名如 `AvaSheet-1.0.0-linux-arm64.tar.gz`。

两个架构都用单文件自包含发布，目标机器不需要装 .NET 运行时。arm64 用 GitHub 的原生 arm runner，不靠 QEMU 模拟。产物的 glibc 下限是 2.27（SkiaSharp 与 .NET 10 运行时自身的基线），麒麟 V10、统信 UOS 20 都能跑。

本地打 AppImage（arm64 机器上会自动用 `aarch64` 的工具，也可以显式设 `APPIMAGE_ARCH=aarch64`）：

```bash
dotnet publish src/AvaSheet -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o publish
packaging/linux/make-appimage.sh publish AvaSheet.AppImage 1.0.0
```

脚本需要 `mksquashfs`（squashfs-tools）；`appimagetool` 与 AppImage runtime 会自动下载到 `~/.cache/avasheet`。

## 许可证

本项目使用 [Apache License 2.0](LICENSE)。
