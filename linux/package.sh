#!/bin/bash
# Builds a self-contained single-file Lumi wrapped as an AppImage, plus a .deb and .rpm when nfpm is installed.
# LUMI_ARCH picks the CPU, by default the build machine's own:
#   x64   -> dist/Lumi-x86_64.AppImage, dist/Lumi-<ver>-amd64.deb, dist/Lumi-<ver>-x86_64.rpm
#   arm64 -> dist/Lumi-aarch64.AppImage, dist/Lumi-<ver>-arm64.deb, dist/Lumi-<ver>-aarch64.rpm
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION=${APP_VERSION:-0.0.0}

case "$(uname -m)" in
  aarch64|arm64) HOST_ARCH=arm64 ;;
  *) HOST_ARCH=x64 ;;
esac
LUMI_ARCH=${LUMI_ARCH:-$HOST_ARCH}
case "$LUMI_ARCH" in
  x64)   RID=linux-x64;   APPIMAGE_ARCH=x86_64;  DEB_ARCH=amd64; RPM_ARCH=x86_64 ;;
  arm64) RID=linux-arm64; APPIMAGE_ARCH=aarch64; DEB_ARCH=arm64; RPM_ARCH=aarch64 ;;
  *) echo "LUMI_ARCH must be x64 or arm64, not $LUMI_ARCH" >&2; exit 1 ;;
esac

rm -rf dist/linux
dotnet publish linux/Lumi/Lumi.csproj -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
  -p:Version="$VERSION" -o dist/linux

APPDIR=dist/Lumi.AppDir
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin"
cp dist/linux/lumi "$APPDIR/usr/bin/lumi"
cp linux/appimage/AppRun linux/appimage/lumi.desktop "$APPDIR/"
cp docs/brand/avatar.png "$APPDIR/lumi.png"

# appimagetool has to run on the build machine; ARCH tells it which runtime to put in front of the image.
[ "$HOST_ARCH" = arm64 ] && TOOL_ARCH=aarch64 || TOOL_ARCH=x86_64
TOOL=dist/appimagetool-$TOOL_ARCH
if [ ! -x "$TOOL" ]; then
  curl -fsSL -o "$TOOL" "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-$TOOL_ARCH.AppImage"
  chmod +x "$TOOL"
fi
ARCH=$APPIMAGE_ARCH "$TOOL" --appimage-extract-and-run "$APPDIR" "dist/Lumi-$APPIMAGE_ARCH.AppImage"
echo "Built dist/Lumi-$APPIMAGE_ARCH.AppImage"

# .deb and .rpm of the same build, when nfpm is installed (CI installs it).
# nfpm takes the Go name of the CPU (amd64, arm64) and writes x86_64 / aarch64 into the .rpm itself.
if command -v nfpm >/dev/null; then
  export APP_VERSION=$VERSION
  export NFPM_ARCH=$DEB_ARCH
  nfpm package -f packaging/linux/nfpm.yaml -p deb -t "dist/Lumi-${VERSION}-${DEB_ARCH}.deb"
  nfpm package -f packaging/linux/nfpm.yaml -p rpm -t "dist/Lumi-${VERSION}-${RPM_ARCH}.rpm"
  echo "Built dist/Lumi-${VERSION}-${DEB_ARCH}.deb and dist/Lumi-${VERSION}-${RPM_ARCH}.rpm"
fi
