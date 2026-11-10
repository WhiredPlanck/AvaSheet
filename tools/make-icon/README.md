# 图标生成

应用图标由 `src/AvaSheet/Assets/avasheet-logo.svg`（1024×1024 母版）生成，脚本在本目录。

```bash
python3 tools/make-icon/make_icon.py
```

跑完覆盖 `src/AvaSheet/Assets/avasheet.ico`，然后重新构建。

## 依赖

- `rsvg-convert`（Arch: `librsvg`，Debian/Ubuntu: `librsvg2-bin`）
- Pillow（`pip install pillow`）

## 改图标

- **换颜色**：改母版里的 `fill="#4F46E5"`（底板）与 `stroke="#FFFFFF"`（图形）。
  深浅色的区分交给桌面环境，不要把浅色/深色做成两套图标。
- **换图形**：母版里 `<g>` 内的 path 一起换掉。图形保持 24×24 的坐标系、
  描边而不是填充（`fill="none"` + `stroke`），脚本靠 `translate`/`scale` 把它放大 33.6 倍。
- 改完母版别忘了同步 `src/AvaSheet/Styles/Icons.axaml` 里的 `IconLogo`
  （窗口内的小徽标用的是 `StreamGeometry`，不是这个 SVG）和
  `Views/MainWindow.axaml`、`Views/AboutWindow.axaml` 里的笔画粗细。

## 为什么分成母版和紧凑版

母版里有六道描边，直接缩到 16/24/32 像素会和底板挤在一起、糊成一片。
所以 48 像素以上用母版，16/24/32 用脚本里的 `SMALL_SVG`（图形放大到 78%、笔画略粗），
16 像素再用 `TINY_SVG`（去掉两道短横线）。三份图形是同一套 path，改的时候要一起改。

## 为什么是 BMP 条目

ICO 支持 PNG 压缩条目和 BMP(DIB) 条目。这里全部写 BMP：Skia、Windows 资源管理器、
Alt+Tab 切换器和各路图标工具都读得懂，PNG 条目在部分旧工具里会显示空白。
每帧是 40 字节的 `BITMAPINFOHEADER`（**高度写两倍**，因为 DIB 把颜色数据和
1 位 AND 掩码算在同一张位图里）+ 自下而上的 BGRA 行 + 每行 4 字节对齐的掩码。
