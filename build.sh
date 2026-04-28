#!/bin/bash
# Build script for FastPickup mod.
# Requires the VINTAGE_STORY environment variable pointing to the VS install directory.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MOD_NAME="FastPickup"
VERSION=$(grep -oP '"version":\s*"\K[^"]+' "$SCRIPT_DIR/modinfo.json")
ZIP_PATH="$SCRIPT_DIR/Releases/${MOD_NAME}_v${VERSION}.zip"
MODS_DIR="$HOME/.config/VintagestoryData/Mods"

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m'

if [ -z "$VINTAGE_STORY" ]; then
    echo -e "${RED}Error: VINTAGE_STORY environment variable is not set.${NC}"
    echo "Set it to your VS installation path, e.g.:"
    echo "  export VINTAGE_STORY=/opt/vintagestory"
    exit 1
fi

echo -e "${GREEN}Building $MOD_NAME v$VERSION...${NC}"

dotnet build "$SCRIPT_DIR/$MOD_NAME.csproj" -c Release

echo ""
echo -e "${GREEN}Build successful!${NC}"
echo "Release zip: $ZIP_PATH"
echo ""
echo "Contents:"
unzip -l "$ZIP_PATH"

# Optional: install directly into VS mods folder.
if [ "${1}" = "--install" ]; then
    if [ -d "$MODS_DIR" ]; then
        cp "$ZIP_PATH" "$MODS_DIR/"
        echo -e "${GREEN}Installed to $MODS_DIR/${NC}"
    else
        echo -e "${YELLOW}Mods directory not found at $MODS_DIR — copy the zip manually.${NC}"
    fi
fi
