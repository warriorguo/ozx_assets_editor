#!/usr/bin/env bash
#
# Build OAE.app — runs `dotnet publish` for the host architecture, lays out
# the macOS bundle, copies the published single-file binary into MacOS/,
# adds Info.plist + PkgInfo, ad-hoc signs.
#
# Usage:
#   ./packaging/macos/build.sh            # detect host arch
#   RID=osx-arm64 ./packaging/macos/build.sh
#
# Output: packaging/macos/dist/OAE.app

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
APP_NAME="OAE"
BUNDLE_NAME="OAE.app"
PROJECT="$REPO_ROOT/OAE.App/OAE.App.csproj"
DIST_DIR="$REPO_ROOT/packaging/macos/dist"
BUNDLE_DIR="$DIST_DIR/$BUNDLE_NAME"

# Detect RID from host arch if not given.
if [[ -z "${RID:-}" ]]; then
    case "$(uname -m)" in
        arm64)  RID=osx-arm64 ;;
        x86_64) RID=osx-x64 ;;
        *)      echo "Unsupported arch: $(uname -m)"; exit 1 ;;
    esac
fi

PUBLISH_DIR="$REPO_ROOT/OAE.App/bin/Release/net10.0/$RID/publish"

echo "== Publishing OAE.App for $RID =="
dotnet publish "$PROJECT" \
    -c Release \
    -r "$RID" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    --nologo

if [[ ! -x "$PUBLISH_DIR/$APP_NAME" ]]; then
    echo "ERROR: published binary not found at $PUBLISH_DIR/$APP_NAME"
    ls -la "$PUBLISH_DIR" || true
    exit 1
fi

echo "== Assembling $BUNDLE_NAME =="
rm -rf "$BUNDLE_DIR"
mkdir -p "$BUNDLE_DIR/Contents/MacOS"
mkdir -p "$BUNDLE_DIR/Contents/Resources"

cp "$PUBLISH_DIR/$APP_NAME" "$BUNDLE_DIR/Contents/MacOS/$APP_NAME"
chmod +x "$BUNDLE_DIR/Contents/MacOS/$APP_NAME"

cp "$REPO_ROOT/packaging/macos/Info.plist" "$BUNDLE_DIR/Contents/Info.plist"

printf 'APPL????' > "$BUNDLE_DIR/Contents/PkgInfo"

echo "== Ad-hoc codesigning =="
codesign --force --deep --sign - "$BUNDLE_DIR"
codesign --verify --verbose "$BUNDLE_DIR" || true

echo
echo "== Done =="
echo "Bundle:    $BUNDLE_DIR"
echo "Arch:      $RID"
ls -la "$BUNDLE_DIR/Contents/"
echo
echo "Binary:    $BUNDLE_DIR/Contents/MacOS/$APP_NAME"
echo "Binary size: $(du -h "$BUNDLE_DIR/Contents/MacOS/$APP_NAME" | cut -f1)"
echo "Binary mtime (fresh-install check): $(date -r "$(stat -f %m "$BUNDLE_DIR/Contents/MacOS/$APP_NAME")" '+%Y-%m-%d %H:%M:%S')"
echo
echo "To launch:  open '$BUNDLE_DIR'"
echo "To install: ditto '$BUNDLE_DIR' /Applications/$BUNDLE_NAME  (then check Contents/MacOS/$APP_NAME mtime to confirm)"
