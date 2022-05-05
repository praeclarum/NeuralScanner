#!/bin/bash
set -x
set -e

# Set the version
VERSION_MAJOR=1
VERSION_MINOR=0
VERSION_PATCH=0

# Get the build number
VERSION_BUILD=$1
if [ -z "$VERSION_BUILD" ]; then
    echo "Usage: $0 <build number>"
    exit 1
fi
VERSION_MAJOR_MINOR=$VERSION_MAJOR.$VERSION_MINOR
VERSION_MAJOR_MINOR_PATCH=$VERSION_MAJOR_MINOR.$VERSION_PATCH
VERSION_MAJOR_MINOR_PATCH_BUILD=$VERSION_MAJOR_MINOR_PATCH.$VERSION_BUILD

# Usable versions
VERSION_LONG=$VERSION_MAJOR_MINOR_PATCH_BUILD

VERSION_BUNDLE=$(($VERSION_MAJOR*10000000 + $VERSION_MINOR*100000 + $VERSION_PATCH*10000 + ($VERSION_BUILD%10000)))

[[ $VERSION_PATCH == 0 ]] && VERSION_SHORT=$VERSION_MAJOR_MINOR || VERSION_SHORT=$VERSION_MAJOR_MINOR_PATCH

# Update the version in the source files
# sed -E -i .bak "s:<Version>[0-9]+\\.[0-9]+\\.[0-9]+</Version>:<Version>$VERSION</Version>:g" MeshCaster/MeshCaster.fsproj
/usr/libexec/PlistBuddy -x -c "Set CFBundleShortVersionString $VERSION_SHORT" NeuralScanner/Info.plist
/usr/libexec/PlistBuddy -x -c "Set CFBundleVersion $VERSION_BUNDLE" NeuralScanner/Info.plist
