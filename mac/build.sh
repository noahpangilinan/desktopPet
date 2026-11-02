#!/bin/sh
# Builds mac/build/Desktop Pet.app with the Swift compiler from Apple's
# command line tools (xcode-select --install).
set -e
cd "$(dirname "$0")"

APP="build/Desktop Pet.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

swiftc -O -swift-version 5 -o "$APP/Contents/MacOS/DesktopPet" Sources/*.swift
cp Info.plist "$APP/Contents/Info.plist"

# Copy only each pet's sheet.png and pet.ini.
for dir in ../pets/*/; do
    name=$(basename "$dir")
    if [ -f "$dir/pet.ini" ] && [ -f "$dir/sheet.png" ]; then
        mkdir -p "$APP/Contents/Resources/pets/$name"
        cp "$dir/pet.ini" "$dir/sheet.png" "$APP/Contents/Resources/pets/$name/"
    fi
done

# Ad-hoc signature so macOS will launch it locally.
codesign --force --sign - "$APP" >/dev/null 2>&1 || true
echo "Built $APP"
