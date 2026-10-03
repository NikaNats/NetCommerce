#!/usr/bin/env bash
# Prove the security headers actually reach the wire.
#
# A CSP declared in next.config.ts but never served is decoration. This fetches
# a real response and asserts on the headers the server sent.
set -Eeuo pipefail

# App dir: defaults to this script's parent, so CI can invoke it without knowing
# the layout. The argument exists for the sandbox wrapper, which resolves the path
# itself — inside the sandbox a hardcoded "C:/..." is not a valid directory.
APP_DIR="${1:-$(cd "$(dirname "$0")/.." && pwd)}"
cd "$APP_DIR" || exit 1

# Deliberately unreachable API: we are testing headers, not data.
export API_BASE_URL=http://localhost:59999
export KEYCLOAK_BASE_URL=http://localhost:59998
export KEYCLOAK_REALM=netcommerce
export KEYCLOAK_CLIENT_ID=netcommerce-web
export PUBLIC_ORIGIN=http://localhost:3000
export npm_config_cache=/scratch/.npm
mkdir -p /scratch/.npm

if command -v netstat >/dev/null 2>&1 && netstat -ano 2>/dev/null | grep -q ':3000 .*LISTENING'; then
  echo "ABORT: port 3000 already in use."
  netstat -ano 2>/dev/null | grep ':3000 .*LISTENING'
  exit 1
fi

./node_modules/.bin/next dev --port 3000 > /tmp/hdr-dev.log 2>&1 &
DEV_PID=$!
trap 'kill $DEV_PID 2>/dev/null' EXIT

for i in $(seq 1 90); do
  grep -q "Ready in" /tmp/hdr-dev.log 2>/dev/null && break
  kill -0 $DEV_PID 2>/dev/null || { echo "DEV EXITED"; cat /tmp/hdr-dev.log; exit 1; }
  sleep 1
done
grep -q "Ready in" /tmp/hdr-dev.log || { echo "NOT READY"; cat /tmp/hdr-dev.log; exit 1; }

node scripts/check-headers.mjs