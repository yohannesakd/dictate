#!/bin/zsh
# Creates signing/: a separate keychain with a self-signed code signing certificate for Dictate.
# Run once per Mac. The login keychain is not changed.
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p signing
cd signing
[[ -f dictate-signing.keychain-db ]] && { echo "signing/ already exists"; exit 0; }

cat > cert.cnf <<CNF
[req]
distinguished_name=dn
x509_extensions=ext
prompt=no
[dn]
CN=Dictate Local Signing
[ext]
keyUsage=critical,digitalSignature
extendedKeyUsage=critical,codeSigning
basicConstraints=critical,CA:false
CNF
/usr/bin/openssl req -x509 -newkey rsa:2048 -nodes -keyout key.pem -out cert.pem -days 3650 -config cert.cnf 2>/dev/null
PASSWORD=$(/usr/bin/openssl rand -hex 16)
print -r -- $PASSWORD > keychain-password
chmod 600 keychain-password
/usr/bin/openssl pkcs12 -export -inkey key.pem -in cert.pem -out id.p12 -passout pass:$PASSWORD 2>/dev/null
security create-keychain -p $PASSWORD $PWD/dictate-signing.keychain-db
security set-keychain-settings $PWD/dictate-signing.keychain-db
security unlock-keychain -p $PASSWORD $PWD/dictate-signing.keychain-db
security import id.p12 -k $PWD/dictate-signing.keychain-db -P $PASSWORD -T /usr/bin/codesign >/dev/null
security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k $PASSWORD $PWD/dictate-signing.keychain-db >/dev/null
rm -f id.p12 key.pem
echo "Created signing/dictate-signing.keychain-db"
