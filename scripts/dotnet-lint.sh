#!/usr/bin/env bash
# Build StarfieldExplore and run IL-based Gendarme (altcode fork) + optional dotnet format.
# Requires: .NET 8 SDK.
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
cd "$root"

dotnet tool restore

proj="tools/StarfieldExplore/StarfieldExplore.csproj"
configuration="${CONFIGURATION:-Debug}"

dotnet build "$proj" -c "$configuration"

dll="$root/tools/StarfieldExplore/bin/$configuration/net8.0/StarfieldExplore.dll"
if [[ ! -f "$dll" ]]; then
  echo "Expected DLL not found: $dll" >&2
  exit 1
fi

echo "== Gendarme (altcode.gendarme-tool) on $dll =="
dotnet gendarme --console --severity medium+ "$dll"

if [[ "${FORMAT_CHECK:-}" == "1" ]]; then
  echo "== dotnet format whitespace --verify-no-changes (may fail until EditorConfig matches flush-left partials) =="
  dotnet format whitespace "$proj" --verify-no-changes
fi
