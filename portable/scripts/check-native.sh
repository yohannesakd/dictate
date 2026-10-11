#!/usr/bin/env bash
set -euo pipefail
mkdir -p portable/artifacts/native
# Install display dependencies inside a disposable container.
docker build -q -t dictate-native-check -f portable/NativeChecks/Dockerfile portable/NativeChecks
docker run --rm --init -v "$PWD:/work" -w /work dictate-native-check bash -lc '
set -euo pipefail
export NO_AT_BRIDGE=0
export XDG_DATA_HOME=/tmp/dictate-native-data
export GTK_MODULES=gail:atk-bridge
timeout 120s xvfb-run -a dbus-run-session -- bash -lc '\''
set -euo pipefail
openbox >/tmp/openbox.log 2>&1 &
python3 portable/scripts/native-fixture.py /tmp/dictate-command &
sleep 2
timeout 20s dotnet portable/NativeChecks/bin/Release/net10.0/Dictate.NativeChecks.dll /tmp/dictate-command
dotnet portable/Desktop/bin/Release/net10.0/Dictate.Desktop.dll >/tmp/dictate-app.log 2>&1 &
app=$!
trap "cat /tmp/dictate-app.log" EXIT
sleep 3
window=$(xdotool search --name "^Dictate$" | head -1)
xdotool windowactivate --sync "$window"
import -window "$window" portable/artifacts/native/home.png
xdotool mousemove --window "$window" 120 100 click 1
sleep 1
import -window "$window" portable/artifacts/native/history.png
xdotool mousemove --window "$window" 200 100 click 1
sleep 1
import -window "$window" portable/artifacts/native/settings.png
xdotool mousemove --window "$window" 125 215 click 1
xdotool key Down Return
sleep 1
python3 -c "import json,os; assert json.load(open(os.environ[\"XDG_DATA_HOME\"]+\"/dictate/settings.json\"))[\"speech_engine\"] == \"whistle\""
kill "$app"
wait "$app" || true
dotnet portable/Desktop/bin/Release/net10.0/Dictate.Desktop.dll >/tmp/dictate-app.log 2>&1 &
app=$!
sleep 3
window=$(xdotool search --name "^Dictate$" | head -1)
xdotool windowactivate --sync "$window"
xdotool mousemove --window "$window" 200 100 click 1
sleep 1
import -window "$window" portable/artifacts/native/whistle-restart.png
echo "Linux speech engine selection persisted across restart."
sha256sum portable/artifacts/native/*.png
kill "$app"
cat /tmp/dictate-app.log
'\''
'
