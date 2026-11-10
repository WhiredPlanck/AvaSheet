#!/usr/bin/env python3
"""从母版 SVG 生成应用图标 avasheet.ico。

用法（在仓库任意位置执行都可以）：

    python3 tools/make-icon/make_icon.py

依赖：
    - rsvg-convert（librsvg，Arch 上是 librsvg 包）
    - Pillow（pip install pillow）

母版在 src/AvaSheet/Assets/avasheet-logo.svg。改颜色或形状只改母版，
然后重跑这个脚本覆盖 src/AvaSheet/Assets/avasheet.ico。

小尺寸（16/24/32）单独用紧凑版：图形放大到 78%、笔画略粗，
16 像素那一帧还去掉了两道短横线，否则缩下去会糊成一团。

ICO 里写成 BMP(DIB) 条目而不是 PNG 条目，兼容性最好——
Skia、Windows 资源管理器、Alt+Tab 和各路图标工具都能读。
"""
import io
import shutil
import struct
import subprocess
import sys
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    print("缺少 Pillow，先运行：pip install pillow", file=sys.stderr)
    raise SystemExit(1)

REPO = Path(__file__).resolve().parents[2]
ASSETS = REPO / "src" / "AvaSheet" / "Assets"
MASTER = ASSETS / "avasheet-logo.svg"
TARGET = ASSETS / "avasheet.ico"
WORK = REPO / "obj" / "icon"

# 母版缩到这些尺寸仍然清楚。
LARGE_SIZES = [48, 64, 128, 256]

# 紧凑版：图形占 78%，笔画 2.2（母版是 2）。
SMALL_SVG = """<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024">
  <rect width="1024" height="1024" rx="232" fill="#4F46E5" />
  <g transform="translate(32 32) scale(40)"
     fill="none" stroke="#FFFFFF" stroke-width="2.2"
     stroke-linecap="round" stroke-linejoin="round">
    <path d="M8 21h-2a3 3 0 0 1 -3 -3v-1h5.5" />
    <path d="M17 8.5v-3.5a2 2 0 1 1 2 2h-2" />
    <path d="M19 3h-11a3 3 0 0 0 -3 3v11" />
    <path d="M9 7h4" />
    <path d="M9 11h4" />
    <path d="M18.42 12.61a2.1 2.1 0 0 1 2.97 2.97l-6.39 6.42h-3v-3l6.42 -6.39" />
  </g>
</svg>
"""

# 16 像素专用：去掉两道短横线，图形再放大一点。
TINY_SVG = """<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024">
  <rect width="1024" height="1024" rx="232" fill="#4F46E5" />
  <g transform="translate(58 56) scale(38)"
     fill="none" stroke="#FFFFFF" stroke-width="2.1"
     stroke-linecap="round" stroke-linejoin="round">
    <path d="M8 21h-2a3 3 0 0 1 -3 -3v-1h5.5" />
    <path d="M17 8.5v-3.5a2 2 0 1 1 2 2h-2" />
    <path d="M19 3h-11a3 3 0 0 0 -3 3v11" />
    <path d="M18.42 12.61a2.1 2.1 0 0 1 2.97 2.97l-6.39 6.42h-3v-3l6.42 -6.39" />
  </g>
</svg>
"""


def render(svg: Path, size: int) -> Image.Image:
    out = WORK / f"{svg.stem}-{size}.png"
    subprocess.run(
        ["rsvg-convert", "-w", str(size), "-h", str(size), "-o", str(out), str(svg)],
        check=True,
    )
    image = Image.open(out).convert("RGBA")
    if image.size != (size, size):
        image = image.resize((size, size), Image.LANCZOS)
    return image


def dib_entry(image: Image.Image) -> bytes:
    """一帧 32 位 BGRA 的 DIB，末尾按 ICO 要求补 1 位 AND 掩码。"""
    width, height = image.size
    pixels = image.load()

    # 高度写两倍：ICO 里的 DIB 把颜色数据和掩码叠在同一张位图里描述。
    header = struct.pack("<IiiHHIIiiII", 40, width, height * 2, 1, 32, 0, 0, 0, 0, 0, 0)

    rows = []
    for y in range(height - 1, -1, -1):
        row = bytearray()
        for x in range(width):
            r, g, b, a = pixels[x, y]
            row += bytes((b, g, r, a))
        rows.append(bytes(row))

    mask_row = ((width + 31) // 32) * 4
    mask = b"\x00" * (mask_row * height)

    return header + b"".join(rows) + mask


def write_ico(entries: list[tuple[int, bytes]], path: Path) -> None:
    out = io.BytesIO()
    out.write(struct.pack("<HHH", 0, 1, len(entries)))

    offset = 6 + 16 * len(entries)
    for size, payload in entries:
        out.write(
            struct.pack(
                "<BBBBHHII",
                0 if size >= 256 else size,
                0 if size >= 256 else size,
                0,
                0,
                1,
                32,
                len(payload),
                offset,
            )
        )
        offset += len(payload)

    for _, payload in entries:
        out.write(payload)

    path.write_bytes(out.getvalue())


def main() -> int:
    if not MASTER.exists():
        print(f"缺少母版：{MASTER}", file=sys.stderr)
        return 1

    if shutil.which("rsvg-convert") is None:
        print("缺少 rsvg-convert，先安装 librsvg（Debian/Ubuntu: librsvg2-bin）", file=sys.stderr)
        return 1

    WORK.mkdir(parents=True, exist_ok=True)

    tiny = WORK / "avasheet-tiny.svg"
    tiny.write_text(TINY_SVG, encoding="utf-8")
    small = WORK / "avasheet-small.svg"
    small.write_text(SMALL_SVG, encoding="utf-8")

    entries = [(16, dib_entry(render(tiny, 16)))]
    for size in (24, 32):
        entries.append((size, dib_entry(render(small, size))))
    for size in LARGE_SIZES:
        entries.append((size, dib_entry(render(MASTER, size))))

    write_ico(entries, TARGET)
    print(f"{TARGET.relative_to(REPO)}  {TARGET.stat().st_size} 字节，{len(entries)} 帧")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
