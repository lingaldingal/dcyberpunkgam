#!/usr/bin/env bash
# Run the game with the C# (Mono) Godot binary, building the C# first.
#
# Usage:
#   ./run.sh            # build C# + run the game (menu)
#   ./run.sh --no-build # skip the C# build (faster, when code hasn't changed)
set -euo pipefail
cd "$(dirname "$0")"

GODOT=/home/bigal/Applications/GodotMono/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64

if [[ "${1:-}" != "--no-build" ]]; then
  echo ">> building C# (dcyberpunkgam)..."
  dotnet build dcyberpunkgam.csproj --nologo -v quiet
fi

echo ">> launching game (menu)..."
exec "$GODOT" --path . "$@"
