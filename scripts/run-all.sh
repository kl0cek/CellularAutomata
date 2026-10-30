#!/usr/bin/env bash
# Automatyczne uruchomienie wersji serwerowej dla wielu plików konfiguracyjnych (Lab 6).
# Każdy config liczy się po kolei we własnym folderze OutputTXT/batch/<nazwa>/,
# tam trafiają Micro.txt, StepTimes.txt i Time.txt (ścieżki w configach są względne).
#
# Użycie (z folderu CellularAutomata):
#   scripts/run-all.sh configs/*.json
set -euo pipefail

if [ $# -eq 0 ]; then
  echo "Użycie: scripts/run-all.sh plik1.json [plik2.json ...]" >&2
  exit 1
fi

root=$(cd "$(dirname "$0")/.." && pwd)
dll="$root/bin/Release/net10.0/CellularAutomata.dll"
dotnet build "$root/CellularAutomata.csproj" -c Release --nologo -v quiet > /dev/null

for config in "$@"; do
  name=$(basename "$config" .json)
  out="$root/OutputTXT/batch/$name"
  mkdir -p "$out"
  cp "$config" "$out/config.json"
  echo "=== $name"
  (cd "$out" && dotnet "$dll" config.json) | grep -E "Symulacja razem|Ziarna na końcu|Błąd" || true
done

echo "Wyniki: $root/OutputTXT/batch/"
