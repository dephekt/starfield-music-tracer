#!/usr/bin/env bash
# Extract named .pex from Starfield - Misc.ba2 and decompile with Champollion (Wine).
# Reuses extract_misc_ba2_script.py; output defaults to research/decompiled/ (gitignored).
#
# Usage:
#   ./tools/decompile_misc_pex.sh                    # preset: organic-research bundle
#   ./tools/decompile_misc_pex.sh --preset minimal # harvester trio + sq_parent only
#   ./tools/decompile_misc_pex.sh sq_parentscript.pex   # one script (basename ok)
#   STARFIELD_DATA=/path/to/Data ./tools/decompile_misc_pex.sh --dry-run
#
# Champollion: prefers `champollion` on PATH (~/.local/bin/champollion), else wine + CHAMPOLLION_EXE
# or ~/.local/share/champollion/Champollion-1.3.2/Champollion.exe
#
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DATA="${STARFIELD_DATA:-$HOME/.steam/steam/steamapps/common/Starfield/Data}"
PE_DIR="${PEX_OUT:-$ROOT/research/decompiled/pe}"
PSC_DIR="${PSC_OUT:-$ROOT/research/decompiled/psc}"

# Basenames under scripts/
PRESET_MINIMAL=(
  outpostharvesterfaunascript.pex
  outpostharvesterflorascript.pex
  outpostharvesterfloraplanterscript.pex
  sq_parentscript.pex
)
# + follow-ons for scan UI / container (step 3–4 of organic research trail)
PRESET_ORGANIC_RESEARCH=(
  "${PRESET_MINIMAL[@]}"
  planettraitscantargetscript.pex
  outpostcontainerscript.pex
)

dry_run=0
preset="organic-research"
files=()

usage() {
  cat <<'EOF'
Extract .pex from Starfield - Misc.ba2 and decompile with Champollion.

Usage:
  ./tools/decompile_misc_pex.sh [--data DIR] [--preset NAME] [--dry-run] [script.pex ...]

Presets (when no positional args):
  organic-research  harvester trio + sq_parent + planettraitscantarget + outpostcontainer (default)
  minimal           harvester trio + sq_parent only

Env: STARFIELD_DATA, PEX_OUT, PSC_OUT, CHAMPOLLION_EXE
EOF
  exit 0
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help) usage ;;
    --dry-run) dry_run=1; shift ;;
    --data)
      DATA="$2"
      shift 2
      ;;
    --preset)
      preset="$2"
      shift 2
      ;;
    *)
      files+=("$1")
      shift
      ;;
  esac
done

normalize_archive_path() {
  local x="$1"
  if [[ "$x" == scripts/* ]]; then
    echo "$x"
    return
  fi
  local base="$x"
  [[ "$base" == *.pex ]] || base="${base}.pex"
  echo "scripts/$base"
}

run_champollion() {
  if command -v champollion >/dev/null 2>&1; then
    champollion "$@"
  else
    local exe="${CHAMPOLLION_EXE:-$HOME/.local/share/champollion/Champollion-1.3.2/Champollion.exe}"
    wine "$exe" "$@"
  fi
}

if [[ ${#files[@]} -eq 0 ]]; then
  case "$preset" in
    minimal) files=("${PRESET_MINIMAL[@]}") ;;
    organic-research) files=("${PRESET_ORGANIC_RESEARCH[@]}") ;;
    *)
      echo "Unknown --preset $preset (use: minimal, organic-research)" >&2
      exit 1
      ;;
  esac
fi

declare -a archive_paths=()
declare -a pe_disk_paths=()
for f in "${files[@]}"; do
  ap="$(normalize_archive_path "$f")"
  archive_paths+=("$ap")
  pe_disk_paths+=("$PE_DIR/$(basename "$ap")")
done

if [[ "$dry_run" -eq 1 ]]; then
  echo "DATA=$DATA"
  echo "PE_DIR=$PE_DIR"
  echo "PSC_DIR=$PSC_DIR"
  echo "Extract:"
  for ap in "${archive_paths[@]}"; do
    echo "  python3 tools/extract_misc_ba2_script.py --data ... --name $ap -o $PE_DIR/$(basename "$ap")"
  done
  echo "Decompile:"
  echo "  champollion|wine -p $PSC_DIR ${pe_disk_paths[*]}"
  exit 0
fi

mkdir -p "$PE_DIR" "$PSC_DIR"

for i in "${!archive_paths[@]}"; do
  ap="${archive_paths[$i]}"
  out="${pe_disk_paths[$i]}"
  python3 "$ROOT/tools/extract_misc_ba2_script.py" --data "$DATA" --name "$ap" -o "$out"
done

run_champollion -p "$PSC_DIR" "${pe_disk_paths[@]}"
echo "PSC output: $PSC_DIR"
