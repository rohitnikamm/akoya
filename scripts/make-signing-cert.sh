#!/bin/bash
# Creates a self-signed code-signing identity "Silk Self-Signed" in the login keychain.
# A stable identity keeps macOS privacy permissions valid across rebuilds.
set -euo pipefail

NAME="Silk Self-Signed"
KEYCHAIN="$HOME/Library/Keychains/login.keychain-db"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

cat > "$TMP/openssl.cnf" <<CNF
[req]
distinguished_name = dn
x509_extensions = ext
prompt = no
[dn]
CN = $NAME
[ext]
basicConstraints = critical,CA:false
keyUsage = critical,digitalSignature
extendedKeyUsage = critical,codeSigning
CNF

openssl req -x509 -newkey rsa:2048 -nodes -days 3650 \
    -keyout "$TMP/key.pem" -out "$TMP/cert.pem" -config "$TMP/openssl.cnf"
openssl pkcs12 -export -inkey "$TMP/key.pem" -in "$TMP/cert.pem" \
    -out "$TMP/silk.p12" -passout pass:silk -name "$NAME"

security import "$TMP/silk.p12" -k "$KEYCHAIN" -P silk -T /usr/bin/codesign
CERT="$HOME/.silk-self-signed.pem"
cp "$TMP/cert.pem" "$CERT"

echo
echo "Imported '$NAME'. Now trust it for code signing by running:"
echo
echo "  security add-trusted-cert -p codeSign -k \"$KEYCHAIN\" \"$CERT\""
