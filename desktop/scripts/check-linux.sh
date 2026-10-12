#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
mkdir -p portable/artifacts/electron-linux
docker build --progress=plain -t dictate-native-check -f portable/NativeChecks/Dockerfile portable/NativeChecks
docker run --rm --init --cap-add=SYS_ADMIN --security-opt seccomp=unconfined \
  -v "$PWD:/work" -v "$(node -p process.execPath):/usr/local/bin/node:ro" \
  -e CHECK_UID="$(id -u)" -e CHECK_GID="$(id -g)" -w /work dictate-native-check bash -lc '
set -euo pipefail
usermod --non-unique --uid "$CHECK_UID" tester
groupmod --non-unique --gid "$CHECK_GID" tester
cp -a desktop/dist/Dictate-linux-x64 /tmp/Dictate
chown -R tester:tester /tmp/Dictate
chown root:root /tmp/Dictate/chrome-sandbox
chmod 4755 /tmp/Dictate/chrome-sandbox
mkdir -p /tmp/test-bin
cat > /tmp/test-bin/arecord <<"EOF"
#!/usr/bin/env python3
import sys,time
while True:
 sys.stdout.buffer.write(bytes(2048));sys.stdout.buffer.flush();time.sleep(.064)
EOF
chmod +x /tmp/test-bin/arecord
chown -R tester:tester /work/portable/artifacts/electron-linux
runuser -u tester -- node desktop/scripts/seed-checks.cjs portable/artifacts/electron-linux/data
runuser -u tester -- env PATH="/tmp/test-bin:$PATH" NO_AT_BRIDGE=0 GTK_MODULES=gail:atk-bridge \
  timeout 120s xvfb-run -a dbus-run-session -- bash -c '\''
set -euo pipefail
openbox >/tmp/dictate-openbox.log 2>&1 &
python3 /work/portable/scripts/native-fixture.py /tmp/dictate-command &
sleep 2
dotnet /work/portable/NativeChecks/bin/Release/net10.0/Dictate.NativeChecks.dll /tmp/dictate-command
/tmp/Dictate/Dictate --data-directory /work/portable/artifacts/electron-linux/data --verify-desktop /work/portable/artifacts/electron-linux/results >/work/portable/artifacts/electron-linux/app.log 2>&1
for engine in whisper; do
 /tmp/Dictate/Dictate --data-directory /work/portable/artifacts/electron-linux/data --verify-desktop /work/portable/artifacts/electron-linux/restart-$engine --verify-restart $engine >>/work/portable/artifacts/electron-linux/app.log 2>&1
 cat /work/portable/artifacts/electron-linux/restart-$engine/checks.txt
done
cat /work/portable/artifacts/electron-linux/results/checks.txt
'\''
'
