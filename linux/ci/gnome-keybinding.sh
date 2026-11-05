#!/bin/bash
# Runs the real Lumi under Xvfb pretending to be a GNOME Wayland session (so it takes the gsettings route) and checks
# that GNOME's custom shortcut ends up stored with a command gsettings accepted: the 2.0.4 bug left it empty.
set -uo pipefail
cd "$(dirname "$0")/../.."
APP=$PWD/dist/linux/lumi
LOG=$PWD/dist/gnome-keybinding.log
mkdir -p dist
export HOME=$(mktemp -d) XDG_CONFIG_HOME=$HOME/.config
export GSETTINGS_BACKEND=keyfile
export XDG_SESSION_TYPE=wayland XDG_CURRENT_DESKTOP=GNOME LUMI_LOG=1

SCHEMA=org.gnome.settings-daemon.plugins.media-keys
gsettings list-keys $SCHEMA > /dev/null || { echo "GNOME media-keys schema missing"; exit 1; }

"$APP" > "$LOG" 2>&1 &
PID=$!
for _ in $(seq 1 30); do grep -q "hotkey: gnome" "$LOG" && break; sleep 1; done
fail=0
check() { # description, actual, expected
  if [ "$2" == "$3" ]; then echo "ok: $1"; else echo "FAIL: $1: got [$2], want [$3]"; fail=1; fi
}
ENTRY=$SCHEMA.custom-keybinding:/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/lumi/
check "list has Lumi" "$(gsettings get $SCHEMA custom-keybindings)" "['/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/lumi/']"
check "name" "$(gsettings get $ENTRY name)" "'Lumi'"
check "command" "$(gsettings get $ENTRY command)" "'\"$APP\" --toggle'"
check "binding" "$(gsettings get $ENTRY binding)" "'<Control>space'"
kill $PID 2>/dev/null
[ $fail == 0 ] || { echo "--- lumi log"; cat "$LOG"; }
exit $fail
