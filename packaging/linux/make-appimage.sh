#!/usr/bin/env bash
# 把一个 dotnet publish 的输出目录打成 AppImage。
#
#   packaging/linux/make-appimage.sh <publish-dir> <output.AppImage> [version]
#
# appimagetool 从 $APPIMAGETOOL 读，没设就下载到缓存目录。
# 在 CI 里由 release 工作流调用；本地也能直接跑（需要 curl 与可用的网络）。
set -euo pipefail

if [ $# -lt 2 ]; then
    echo "用法：$(basename "$0") <publish-dir> <output.AppImage> [version]" >&2
    exit 2
fi

publish_dir=$(cd "$1" && pwd)
mkdir -p "$(dirname "$2")"
out_dir=$(cd "$(dirname "$2")" && pwd)
out_file="$out_dir/$(basename "$2")"
version=${3:-}
arch=${APPIMAGE_ARCH:-x86_64}

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repo=$(cd "$here/../.." && pwd)

appdir=$(mktemp -d)
trap 'rm -rf "$appdir"' EXIT

# AppImage 的标准布局：程序放 usr/bin，桌面入口与图标放 usr/share。
install -d "$appdir/usr/bin" \
           "$appdir/usr/share/applications" \
           "$appdir/usr/share/icons/hicolor/256x256/apps" \
           "$appdir/usr/share/licenses/AvaSheet"

cp -a "$publish_dir/." "$appdir/usr/bin/"
chmod +x "$appdir/usr/bin/AvaSheet"

install -m 0755 "$here/AppRun" "$appdir/AppRun"

desktop="$appdir/avasheet.desktop"
install -m 0644 "$here/avasheet.desktop" "$desktop"
if [ -n "$version" ]; then
    printf 'X-AppImage-Version=%s\n' "$version" >> "$desktop"
fi

install -m 0644 "$desktop" "$appdir/usr/share/applications/avasheet.desktop"
install -m 0644 "$here/avasheet.png" "$appdir/avasheet.png"
ln -s avasheet.png "$appdir/.DirIcon"
install -m 0644 "$here/avasheet.png" "$appdir/usr/share/icons/hicolor/256x256/apps/avasheet.png"

# Apache-2.0 要求分发时带上许可证全文。
install -m 0644 "$repo/LICENSE" "$appdir/usr/share/licenses/AvaSheet/LICENSE"
if [ -f "$repo/README.md" ]; then
    install -m 0644 "$repo/README.md" "$appdir/usr/share/licenses/AvaSheet/README.md"
fi

# dotnet publish 产物的权限是 0744（只给属主可执行），别的用户解包后跑不起来。
chmod -R a+rX "$appdir"

cache_dir="${XDG_CACHE_HOME:-$HOME/.cache}/avasheet"
mkdir -p "$cache_dir"

tool=${APPIMAGETOOL:-$cache_dir/appimagetool-$arch.AppImage}
if [ ! -x "$tool" ]; then
    echo "下载 appimagetool（$arch）..." >&2
    curl -fsSL --retry 3 --retry-delay 2 -o "$tool" \
        "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-$arch.AppImage"
    chmod +x "$tool"
fi

# appimagetool 每次运行都会临时下载 runtime，网络抖一下整次打包就失败；自己下好传进去。
runtime=${APPIMAGE_RUNTIME:-$cache_dir/runtime-$arch}
if [ ! -s "$runtime" ]; then
    echo "下载 AppImage runtime（$arch）..." >&2
    curl -fsSL --retry 3 --retry-delay 2 -o "$runtime.part" \
        "https://github.com/AppImage/type2-runtime/releases/download/continuous/runtime-$arch"
    mv "$runtime.part" "$runtime"
fi

# appimagetool 自身也是 AppImage，没有 FUSE 的环境靠这个变量自动解包运行。
export APPIMAGE_EXTRACT_AND_RUN=1
export ARCH="$arch"

"$tool" --no-appstream --runtime-file "$runtime" "$appdir" "$out_file"
echo "已生成 $out_file（$(du -h "$out_file" | cut -f1)）"
