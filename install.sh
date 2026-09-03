#!/usr/bin/env sh
set -eu

ROOT_DIR="$(cd "$(dirname "$0")" && pwd)"
UI_DIR="$ROOT_DIR/src/squad-ui"

os="$(uname -s)"
arch="$(uname -m)"
case "$os:$arch" in
  Linux:x86_64|Linux:amd64) RID="linux-x64" ;;
  Darwin:x86_64|Darwin:amd64) RID="osx-x64" ;;
  Darwin:arm64|Darwin:aarch64) RID="osx-arm64" ;;
  *) printf 'Unsupported platform: %s %s\n' "$os" "$arch" >&2; exit 1 ;;
esac

BIN_DIR="$ROOT_DIR/bin"

if [ "$RID" = "linux-x64" ]; then
  if ! command -v ldconfig >/dev/null 2>&1 ||
    ! ldconfig -p 2>/dev/null | grep -q 'libwebkit2gtk-4\.1\.so\.0' ||
    ! ldconfig -p 2>/dev/null | grep -q 'libnotify\.so\.4'; then
    cat >&2 <<'EOF'
Missing Linux prerequisites: libwebkit2gtk-4.1.so.0 and libnotify.so.4.
Install WebKitGTK 4.1 and libnotify, then rerun this script. On Ubuntu/Debian:
  sudo apt update && sudo apt install libwebkit2gtk-4.1-0 libnotify4
EOF
    exit 1
  fi
fi

rm -rf "$BIN_DIR"
mkdir -p "$BIN_DIR"

npm ci --prefix "$UI_DIR"
npm run build --prefix "$UI_DIR"

dotnet publish "$ROOT_DIR/src/squad/squad.csproj" \
  --configuration Release \
  --runtime "$RID" \
  --self-contained true \
  --property:PublishDir="$BIN_DIR/" \
  --nologo

dotnet publish "$ROOT_DIR/src/squad-hq/squad-hq.csproj" \
  --configuration Release \
  --runtime "$RID" \
  --self-contained true \
  --property:PublishDir="$BIN_DIR/" \
  --nologo

test -f "$BIN_DIR/ui/index.html"
test -x "$BIN_DIR/squad"
test -x "$BIN_DIR/squad-hq"
test -f "$BIN_DIR/runtimes/$RID/native/copilot"
case "$RID" in
  linux-x64)
    test -f "$BIN_DIR/Photino.Native.so"
    test -f "$BIN_DIR/runtimes/$RID/native/libcopilot_runtime.so"
    ;;
  osx-*)
    test -f "$BIN_DIR/Photino.Native.dylib"
    test -f "$BIN_DIR/runtimes/$RID/native/libcopilot_runtime.dylib"
    ;;
esac

printf 'BlaXquad installed in %s\nAdd this directory to PATH.\n' "$BIN_DIR"
