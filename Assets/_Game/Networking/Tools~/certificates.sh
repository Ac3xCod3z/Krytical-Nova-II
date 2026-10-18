#!/usr/bin/env bash
# Krytical Nova: the certificates that encrypt direct connections to a dedicated server (online phase 2, milestone 2).
#
#   certificates.sh ca                      makes the master key and certificate (once; refuses if they exist)
#   certificates.sh server <folder>         makes a server key and certificate signed by the master key, into <folder>
#
# Run it in Git Bash. Everything lands in Documents/KryticalNova-Secrets (the owner's pick: never in the project or git;
# back the folder up to a USB stick or a password manager). The master certificate (ca.crt) is public: its text goes into
# Networking/Runtime/ServerEncryption.cs so every game can check the server. ca.key and server.key are secret.
# A server reads server.crt and server.key from the "certificates" folder beside its own folder (ServerLaunchOptions).
set -euo pipefail
export MSYS_NO_PATHCONV=1   # Git Bash would turn "/CN=..." into a Windows path

SECRETS="${KN_SECRETS:-$USERPROFILE/Documents/KryticalNova-Secrets}"
SERVER_NAME="krytical-nova-server"   # the name every game checks (ServerEncryption.ServerName)
mkdir -p "$SECRETS"
cd "$SECRETS"

case "${1:-}" in
  ca)
    if [ -f ca.key ]; then echo "The master key already exists in $SECRETS: not replaced."; exit 1; fi
    openssl req -x509 -newkey rsa:2048 -nodes -keyout ca.key -out ca.crt -days 7300 \
      -subj "/CN=Krytical Nova certificate authority" \
      -addext "basicConstraints=critical,CA:TRUE" -addext "keyUsage=critical,keyCertSign,cRLSign" 2>/dev/null
    echo "Master key and certificate made in $SECRETS (ca.key is secret)."
    ;;
  server)
    dest="${2:?give the folder the server reads its certificate from}"
    [ -f ca.key ] || { echo "No master key in $SECRETS: run 'certificates.sh ca' first."; exit 1; }
    openssl req -newkey rsa:2048 -nodes -keyout server.key -out server.csr -subj "/CN=$SERVER_NAME" 2>/dev/null
    printf "subjectAltName=DNS:%s\nbasicConstraints=CA:FALSE\nkeyUsage=critical,digitalSignature,keyEncipherment\nextendedKeyUsage=serverAuth\n" "$SERVER_NAME" > server.ext
    openssl x509 -req -in server.csr -CA ca.crt -CAkey ca.key -CAcreateserial -out server.crt -days 1825 -extfile server.ext 2>/dev/null
    rm -f server.csr server.ext
    mkdir -p "$dest"
    cp server.crt server.key "$dest/"
    echo "Server key and certificate made and copied to $dest (server.key is secret)."
    ;;
  *)
    sed -n '2,10p' "$0"; exit 1 ;;
esac
