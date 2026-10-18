#!/bin/bash
set -euo pipefail

# Package Argo Books as a Linux AppImage with Velopack, plus the update package the app downloads.
#
# Usage:
#   From the repo root (will build and package):
#     ./packaging/linux/build-appimage.sh [version]
#
#   From any folder with publish/linux-x64/ and packaging/linux/ (package only):
#     ./packaging/linux/build-appimage.sh <version>
#
# Prerequisites:
#   - the vpk tool at the Velopack version in Directory.Packages.props
#   - A 256x256 PNG icon at packaging/linux/com.argobooks.ArgoBooks.png

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
ARCH="x86_64"
PUBLISH_DIR="$ROOT_DIR/publish/linux-x64"

# Determine version
if [ -n "${1:-}" ]; then
    VERSION="$1"
elif [ -f "$ROOT_DIR/Directory.Build.props" ]; then
    VERSION=$(grep -oP '<Version>\K[^<]+' "$ROOT_DIR/Directory.Build.props" | head -1)
    if [ -z "$VERSION" ]; then
        echo "Error: Could not determine version. Pass it as an argument: ./build-appimage.sh 2.0.5"
        exit 1
    fi
else
    echo "Error: No version provided and Directory.Build.props not found."
    echo "Usage: ./build-appimage.sh <version>"
    exit 1
fi

echo "Packaging Argo Books v${VERSION} AppImage for ${ARCH}..."

# Build if .NET SDK is available and the repo is present, otherwise expect pre-built output
if [ -f "$ROOT_DIR/ArgoBooks.Desktop/ArgoBooks.Desktop.csproj" ] && command -v dotnet &> /dev/null; then
    echo "Building .NET application..."
    dotnet publish "$ROOT_DIR/ArgoBooks.Desktop" \
        -c Release \
        -f net10.0 \
        -r linux-x64 \
        --self-contained \
        -o "$PUBLISH_DIR"
elif [ ! -d "$PUBLISH_DIR" ]; then
    echo "Error: No published output found at $PUBLISH_DIR"
    echo "Either run from the repo root with .NET SDK installed, or copy publish/linux-x64/ here first."
    exit 1
else
    echo "Using pre-built output from $PUBLISH_DIR"
fi

# Create AppDir structure
# The folder name ends in .AppDir so Velopack packs it as built, keeping the .desktop entry and .argo file type below.
APPDIR="$ROOT_DIR/ArgoBooks.AppDir"
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin"
mkdir -p "$APPDIR/usr/share/applications"
mkdir -p "$APPDIR/usr/share/mime/packages"
mkdir -p "$APPDIR/usr/share/icons/hicolor/256x256/apps"

# Copy published files
echo "Copying published files to AppDir..."
cp -r "$PUBLISH_DIR"/* "$APPDIR/usr/bin/"

# Copy desktop entry and MIME type
cp "$SCRIPT_DIR/com.argobooks.ArgoBooks.desktop" "$APPDIR/"
cp "$SCRIPT_DIR/com.argobooks.ArgoBooks.desktop" "$APPDIR/usr/share/applications/"
cp "$SCRIPT_DIR/com.argobooks.ArgoBooks.xml" "$APPDIR/usr/share/mime/packages/"

# Copy icon (must be a PNG file)
ICON_FILE="$SCRIPT_DIR/com.argobooks.ArgoBooks.png"
if [ ! -f "$ICON_FILE" ]; then
    echo "Error: Icon file not found at $ICON_FILE"
    echo "Please provide a 256x256 PNG icon at: packaging/linux/com.argobooks.ArgoBooks.png"
    exit 1
fi
cp "$ICON_FILE" "$APPDIR/"
cp "$ICON_FILE" "$APPDIR/usr/share/icons/hicolor/256x256/apps/"

# Create AppRun symlink
ln -sf usr/bin/"Argo Books" "$APPDIR/AppRun"

# Make the main executable actually executable
chmod +x "$APPDIR/usr/bin/Argo Books"

# Build the AppImage with Velopack, which adds its updater beside the app.
if ! command -v vpk &> /dev/null; then
    echo "Error: vpk is not on PATH. Install it at the Velopack version in Directory.Packages.props:"
    echo "  dotnet tool install --global vpk --version <version>"
    exit 1
fi

VELOPACK_DIR="$ROOT_DIR/publish/velopack/linux"
rm -rf "$VELOPACK_DIR"
# --delta None: only whole packages carry a signature the app checks, so partial updates are never built.
vpk pack \
    --packId ArgoBooksApp \
    --packVersion "$VERSION" \
    --packDir "$APPDIR" \
    --mainExe "Argo Books" \
    --packTitle "Argo Books" \
    --packAuthors "Argo Books" \
    --runtime linux-x64 \
    --channel linux \
    --delta None \
    --outputDir "$VELOPACK_DIR"

OUTPUT_FILE="$ROOT_DIR/publish/ArgoBooks-${VERSION}-linux-x64.AppImage"
cp "$VELOPACK_DIR/ArgoBooksApp.AppImage" "$OUTPUT_FILE"

# Clean up
rm -rf "$APPDIR"

echo ""
echo "AppImage created: $OUTPUT_FILE"
echo "Update files: $VELOPACK_DIR"
echo "Size: $(du -h "$OUTPUT_FILE" | cut -f1)"
