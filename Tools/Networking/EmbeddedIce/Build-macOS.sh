#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != Darwin ]]; then
  echo "Run this script on macOS with Xcode command line tools, CMake and Git." >&2
  exit 1
fi
install_plugin=false
if [[ $# -eq 1 && "$1" == --install-plugin ]]; then
  install_plugin=true
elif [[ $# -ne 0 ]]; then
  echo "Usage: bash Build-macOS.sh [--install-plugin]" >&2
  exit 1
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../.." && pwd)"
revision=b89c792e3612faf2f12cf35bcc56857313a06be3
source_dir="$repo_root/LocalData/EmbeddedIce/libjuice-v1.7.4"
build_dir="$repo_root/LocalData/EmbeddedIce/native-build-macos"
if [[ ! -d "$source_dir" ]]; then
  git clone --quiet --depth 1 --branch v1.7.4 https://github.com/paullouisageneau/libjuice.git "$source_dir"
fi
if [[ "$(git -C "$source_dir" rev-parse HEAD)" != "$revision" || -n "$(git -C "$source_dir" status --porcelain)" ]]; then
  echo "The pinned libjuice source is missing or modified." >&2
  exit 1
fi

cmake -S "$script_dir" -B "$build_dir" -DCMAKE_BUILD_TYPE=Release \
  "-DLIBJUICE_SOURCE=$source_dir" '-DCMAKE_OSX_ARCHITECTURES=arm64;x86_64' \
  -DCMAKE_OSX_DEPLOYMENT_TARGET=11.0
cmake --build "$build_dir" --parallel 2
plugin="$build_dir/libmoon_ice.dylib"
lipo -verify_arch arm64 x86_64 "$plugin"
codesign --force --sign - "$plugin"
codesign --verify --strict "$plugin"
ctest --test-dir "$build_dir" --output-on-failure
otool -L "$plugin"
shasum -a 256 "$plugin"

if $install_plugin; then
  destination="$repo_root/Assets/Plugins/EmbeddedIce/macOS"
  mkdir -p "$destination"
  cp "$plugin" "$destination/libmoon_ice.dylib"
fi
