#!/usr/bin/env python3
"""Run the actual build script with fake compilers and signing tools. Read no credentials."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile

repo = Path(__file__).resolve().parents[1]
root = Path(tempfile.mkdtemp(prefix='build-check-', dir='/tmp/dictate-sol-fixes'))
(root / 'Sources').mkdir()
(root / 'Sources' / 'Fake.swift').write_text('')
(root / 'patches').mkdir()
shutil.copy(repo / 'patches/phonon-frame-capacity.patch', root / 'patches')
shutil.copy(repo / 'build.sh', root)
shutil.copy(repo / 'Info.plist', root)
# The patch's removed lines reconstruct the two original source sections for this shell-only check.
patch = (repo / 'patches/phonon-frame-capacity.patch').read_text().splitlines()
source = []
for line in patch:
    if line.startswith(' ') or (line.startswith('-') and not line.startswith('---')):
        source.append(line[1:])
vendor = root / 'vendor/phonon-coreml/Sources/PhononCoreML'
vendor.mkdir(parents=True)
(vendor / 'Transcriber.swift').write_text('\n'.join(source) + '\n')
model = root / 'models/Phonon-2-CoreML'
model.mkdir(parents=True)
for name in ['manifest.json', 'decoder.bin', 'NOTICE', 'LICENSE-WEIGHTS-CC-BY-4.0.txt']:
    (model / name).write_text('generated fixture')
(model / 'Phonon-2.mlpackage').mkdir()
(root / 'signing').mkdir()
(root / 'signing/keychain-password').write_text('fake password')
bin_dir = root / 'bin'
bin_dir.mkdir()
tools = {
    'xcrun': '''#!/bin/zsh
print "$*" >> "$CHECK_LOG"
output=""
for ((i=1; i<=$#; i++)); do
  if [[ "${argv[$i]}" == -o ]]; then output="${argv[$((i+1))]}"; fi
done
if [[ "$output" == *libPhononCoreML.a ]]; then stage=runtime; else stage=app; fi
[[ "$FAIL_STAGE" != "$stage" ]] || exit 41
mkdir -p "${output:h}"
print 'new artifact' > "$output"
''',
    'security': '''#!/bin/zsh
if [[ "$1" == list-keychains && "$*" != *" -s "* ]]; then print '"fake-original.keychain"'; fi
if [[ "$1" == find-identity ]]; then print '1) ABCDEF "Dictate Local Signing"'; fi
''',
    'codesign': '''#!/bin/zsh
if [[ "$1" == --verify ]]; then stage=verify; else stage=sign; fi
[[ "$FAIL_STAGE" != "$stage" ]] || exit 42
''',
    'cp': '''#!/bin/zsh
args=()
for arg in "$@"; do [[ "$arg" == -c ]] || args+=("$arg"); done
/bin/cp "${args[@]}"
''',
    'mv': '''#!/bin/zsh
if [[ "$FAIL_STAGE" == publish && "$1" == */Dictate.app && "$2" == build/Dictate.app ]]; then exit 43; fi
/bin/mv "$@"
''',
}
for name, script in tools.items():
    path = bin_dir / name
    path.write_text(script)
    path.chmod(0o755)
env = os.environ | {'PATH': str(bin_dir) + ':' + os.environ['PATH'], 'CHECK_LOG': str(root / 'commands.log')}
for stage in ['runtime', 'app', 'sign', 'verify', 'publish', 'none', 'none']:
    app = root / 'build/Dictate.app'
    app.mkdir(parents=True, exist_ok=True)
    (app / 'previous-marker').write_text('previous valid app')
    result = subprocess.run(['zsh', './build.sh'], cwd=root, env=env | {'FAIL_STAGE': stage}, capture_output=True, text=True)
    (root / f'{stage}-result.log').write_text(result.stdout + result.stderr)
    if stage == 'none':
        assert result.returncode == 0, result.stdout + result.stderr
        assert (app / 'Contents/MacOS/Dictate').read_text().strip() == 'new artifact'
        assert not (app / 'previous-marker').exists()
    else:
        assert result.returncode != 0
        assert (app / 'previous-marker').read_text() == 'previous valid app', stage
    assert not list((root / 'build').glob('.dictate.*')), stage
commands = (root / 'commands.log').read_text()
assert commands.count('-module-name PhononCoreML') == 7
assert 'apple-macosx15.0' in commands
assert 'model(forFrames' not in (vendor / 'Transcriber.swift').read_text()
print('Passed runtime freshness, vendor isolation, deployment target, and five build-failure preservation checks')
print(f'Controlled build evidence: {root}')
