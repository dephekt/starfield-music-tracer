# Source before `dotnet run` on Linux (Steam client + Proton prefix for appid 1716740).
# Copy to `env.local.sh`, adjust paths, and: `source tools/StarfieldExplore/env.local.sh`
#
# Important: the game writes **Plugins.txt** (capital P). Linux paths are case-sensitive;
# `plugins.txt` will not work if the file on disk is `Plugins.txt`.

export STARFIELD_DATA="${STARFIELD_DATA:-$HOME/.steam/steam/steamapps/common/Starfield/Data}"
export STARFIELD_PLUGINS_TXT="${STARFIELD_PLUGINS_TXT:-$HOME/.steam/steam/steamapps/compatdata/1716740/pfx/drive_c/users/steamuser/AppData/Local/Starfield/Plugins.txt}"

# Optional: Mutagen Language enum name (e.g. English, French).
# export STARFIELD_TARGET_LANGUAGE=English

# Optional: absolute path to Starfield.ini when archive / string discovery needs an explicit INI.
# export STARFIELD_INI="$HOME/.../Starfield.ini"
