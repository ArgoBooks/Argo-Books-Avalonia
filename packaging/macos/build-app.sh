#!/bin/bash
set -euo pipefail

# Package Argo Books for macOS with Velopack: a signed, notarized .app in a .zip, plus the update package.
#
# Usage:
#   ./packaging/macos/build-app.sh [version]
#
# Signing and notarization are driven by two environment variables. With neither
# set the script still produces a working bundle, but only for local testing:
# anything downloaded through a browser is quarantined and macOS will refuse to
# open an unsigned app.
#
#   APPLE_SIGN_IDENTITY   "Developer ID Application: Your Name (TEAMID)"
#                         List available ones with: security find-identity -v -p codesigning
#   APPLE_NOTARY_PROFILE  keychain profile name, created once with:
#                           xcrun notarytool store-credentials "argo-notary" \
#                             --apple-id you@example.com --team-id TEAMID \
#                             --password <app-specific-password>
#
# Prerequisites: .NET 10 SDK (arm64), Xcode Command Line Tools, and the vpk tool at the
# Velopack version in Directory.Packages.props (docs/setup/OneTimeReleaseSetup.md).

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
# Apple Silicon by default. RID=osx-x64 builds the Intel one; both ship, and the updater
# picks by architecture, so a release means running this twice.
RID="${RID:-osx-arm64}"
# Release by default. CONFIGURATION=Debug builds the sandbox-facing variant, which is what
# the auto-update test needs: ApiConfig points a Debug build at dev.argorobots.com.
CONFIGURATION="${CONFIGURATION:-Release}"
PUBLISH_DIR="$ROOT_DIR/publish/$RID"
# Finished artefacts sit in publish/, alongside the per-RID folders holding the
# raw compile output this script consumes as input.
OUT_DIR="$ROOT_DIR/publish"
APP_NAME="Argo Books"

# Determine version. BSD grep has no -PCRE, so this uses sed rather than the
# grep -oP the Linux script gets away with on a GNU userland.
if [ -n "${1:-}" ]; then
    VERSION="$1"
elif [ -f "$ROOT_DIR/Directory.Build.props" ]; then
    VERSION=$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' "$ROOT_DIR/Directory.Build.props" | head -1)
    if [ -z "$VERSION" ]; then
        echo "Error: Could not read <Version> from Directory.Build.props. Pass it instead: ./build-app.sh 2.0.15"
        exit 1
    fi
else
    echo "Error: No version provided and Directory.Build.props not found."
    exit 1
fi

echo "Packaging $APP_NAME v${VERSION} for ${RID}..."

# --- Build -------------------------------------------------------------------

if [ -f "$ROOT_DIR/ArgoBooks.Desktop/ArgoBooks.Desktop.csproj" ] && command -v dotnet &> /dev/null; then
    echo "Building .NET application..."
    # net10.0, not the net10.0-windows TFM. PublishReadyToRun applies here too,
    # which is why this is a publish and not a build (see docs/Publishing.md).
    dotnet publish "$ROOT_DIR/ArgoBooks.Desktop" \
        -c "$CONFIGURATION" \
        -f net10.0 \
        -r "$RID" \
        --self-contained \
        -o "$PUBLISH_DIR"
elif [ ! -d "$PUBLISH_DIR" ]; then
    echo "Error: No published output found at $PUBLISH_DIR"
    exit 1
else
    echo "Using pre-built output from $PUBLISH_DIR"
fi

if [ ! -f "$PUBLISH_DIR/$APP_NAME" ]; then
    echo "Error: Expected executable '$PUBLISH_DIR/$APP_NAME' not found."
    echo "The bundle's CFBundleExecutable must match AssemblyName in ArgoBooks.Desktop.csproj."
    exit 1
fi

# --- Assemble the bundle -----------------------------------------------------

mkdir -p "$OUT_DIR"
APP_BUNDLE="$OUT_DIR/$APP_NAME.app"
rm -rf "$APP_BUNDLE"
mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources"

echo "Copying published files into the bundle..."
cp -R "$PUBLISH_DIR"/* "$APP_BUNDLE/Contents/MacOS/"
chmod +x "$APP_BUNDLE/Contents/MacOS/$APP_NAME"

sed "s/__VERSION__/$VERSION/g" "$SCRIPT_DIR/Info.plist" > "$APP_BUNDLE/Contents/Info.plist"

# --- Icon --------------------------------------------------------------------

SOURCE_ICON="$ROOT_DIR/ArgoBooks/Assets/argo-logo.png"
if [ ! -f "$SOURCE_ICON" ]; then
    echo "Error: Source icon not found at $SOURCE_ICON"
    exit 1
fi

ICON_WIDTH=$(sips -g pixelWidth "$SOURCE_ICON" | sed -n 's/.*pixelWidth: *//p')
if [ "${ICON_WIDTH:-0}" -lt 1024 ]; then
    echo "Warning: source icon is ${ICON_WIDTH}px wide. macOS renders app icons up to 1024px,"
    echo "         so anything smaller will look soft in the Dock and Finder."
fi

echo "Generating argo-logo.icns..."
ICONSET="$OUT_DIR/argo-logo.iconset"
rm -rf "$ICONSET"
mkdir -p "$ICONSET"
for SIZE in 16 32 128 256 512; do
    sips -z $SIZE $SIZE           "$SOURCE_ICON" --out "$ICONSET/icon_${SIZE}x${SIZE}.png"      > /dev/null
    sips -z $((SIZE*2)) $((SIZE*2)) "$SOURCE_ICON" --out "$ICONSET/icon_${SIZE}x${SIZE}@2x.png" > /dev/null
done
iconutil -c icns "$ICONSET" -o "$APP_BUNDLE/Contents/Resources/argo-logo.icns"
rm -rf "$ICONSET"

# --- Sign the nested code ---------------------------------------------------

# Copying from a non-macOS filesystem can leave extended attributes behind, and
# codesign fails on a bundle that has them.
xattr -cr "$APP_BUNDLE"

# Debug symbols first: they are not code the app runs, and the .dwarf is itself Mach-O, so
# leaving them in means codesign demanding a signature for something that should never ship.
find "$APP_BUNDLE/Contents/MacOS" \( -name "*.pdb" -o -name "*.dwarf" \) -delete

if [ -n "${APPLE_SIGN_IDENTITY:-}" ]; then

# Sign inside out. `codesign --deep` is documented as unsuitable for anything but
# the simplest bundles and silently misses nested .NET native libraries, so each
# one is signed explicitly before the bundle itself.
#
# That means the managed assemblies too, not just the native libraries. Everything a
# self-contained .NET publish emits lands in Contents/MacOS, codesign treats that directory
# as nested code, and it recognises .NET assemblies as code objects because they are PE
# files. Signing only the .dylibs leaves roughly 260 assemblies unsigned, and sealing the
# bundle then fails on the first one it reaches:
#     Argo Books.app: code object is not signed at all
#     In subcomponent: .../Contents/MacOS/System.Net.Security.dll
NESTED_CODE=$(mktemp)
trap 'rm -f "$NESTED_CODE" "$NESTED_CODE.err"' EXIT

find "$APP_BUNDLE/Contents/MacOS" -type f \
    \( -name "*.dylib" -o -name "*.so" -o -name "*.dll" \) -print >> "$NESTED_CODE"

# Native executables such as createdump carry no extension, so they are found by content.
#
# The bundle's own main executable is excluded. Signing it by path makes codesign resolve it
# to the bundle that contains it and sign that instead, which fails before the libraries are
# ready:
#     Argo Books: code object is not signed at all
#     In subcomponent: .../Argo Books.runtimeconfig.json
# It gets its signature from the bundle signing below, which is the only correct way to sign
# a main executable.
find "$APP_BUNDLE/Contents/MacOS" -type f \
    ! -name "*.dylib" ! -name "*.so" ! -name "*.dll" ! -name "$APP_NAME" \
    -exec sh -c 'file -b "$1" | grep -q "Mach-O" && printf "%s\n" "$1"' _ {} \; >> "$NESTED_CODE"

echo "Signing $(wc -l < "$NESTED_CODE" | tr -d ' ') nested code files..."
while IFS= read -r NESTED_FILE; do
    if ! codesign --force --timestamp --options runtime \
        --sign "$APPLE_SIGN_IDENTITY" "$NESTED_FILE" 2>"$NESTED_CODE.err"; then
        echo "Failed to sign: $NESTED_FILE"
        cat "$NESTED_CODE.err"
        exit 1
    fi
done < "$NESTED_CODE"

fi

# --- Package with Velopack ---------------------------------------------------

# The downloadable zip is copied to publish/ under the name the website and Homebrew expect.
if ! command -v vpk &> /dev/null; then
    echo "Error: vpk is not on PATH. Install it at the Velopack version in Directory.Packages.props:"
    echo "  dotnet tool install --global vpk --version <version>"
    exit 1
fi

VELOPACK_DIR="$OUT_DIR/velopack/$RID"
rm -rf "$VELOPACK_DIR"
PACK_ARGS=(
    pack
    --packId ArgoBooksApp
    --packVersion "$VERSION"
    --packDir "$APP_BUNDLE"
    --mainExe "$APP_NAME"
    --packTitle "$APP_NAME"
    --packAuthors "Argo Books"
    --runtime "$RID"
    --channel "$RID"
    # Only whole packages carry a signature the app checks, so partial updates are never built.
    --delta None
    # People download the zip, so the .pkg installer, which needs a Developer ID Installer certificate, is not built.
    --noInst
    --outputDir "$VELOPACK_DIR"
)
if [ -n "${APPLE_SIGN_IDENTITY:-}" ]; then
    # Velopack wants the certificate name without the "(TEAMID)" that security find-identity prints.
    PACK_ARGS+=(--signAppIdentity "$(echo "$APPLE_SIGN_IDENTITY" | sed 's/ *([A-Z0-9]*)$//')" --signEntitlements "$SCRIPT_DIR/entitlements.plist")
    if [ -n "${APPLE_NOTARY_PROFILE:-}" ]; then
        PACK_ARGS+=(--notaryProfile "$APPLE_NOTARY_PROFILE")
    fi
fi

vpk "${PACK_ARGS[@]}"

OUTPUT_FILE="$OUT_DIR/ArgoBooks-${VERSION}-${RID}.zip"
cp "$VELOPACK_DIR/ArgoBooksApp-${RID}-Portable.zip" "$OUTPUT_FILE"

echo ""
echo "Created: $OUTPUT_FILE"
echo "Update files: $VELOPACK_DIR"
if [ -z "${APPLE_SIGN_IDENTITY:-}" ]; then
    echo "UNSIGNED: it runs on this Mac, but a downloaded copy is rejected as damaged on any other."
elif [ -z "${APPLE_NOTARY_PROFILE:-}" ]; then
    echo "Signed but NOT notarized: Gatekeeper still blocks a first launch from a download."
fi
echo ""
echo "Next: copy publish/velopack/$RID to the Windows release machine and sign it with"
echo "packaging/sign-release.ps1 (docs/Publishing.md, \"Going Live\")."
