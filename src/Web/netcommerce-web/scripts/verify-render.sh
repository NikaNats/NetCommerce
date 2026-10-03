#!/usr/bin/env bash
# Boot the dev server, fetch the rendered page, assert design invariants.
# Verifies the page ACTUALLY renders — a passing build does not prove that.
set -uo pipefail
cd "$(dirname "$0")/.."

export npm_config_cache=/scratch/.npm
# Deliberately unreachable ports: exercises the API-failure rendering path.
export API_BASE_URL=http://localhost:59999
export KEYCLOAK_BASE_URL=http://localhost:59998
export KEYCLOAK_REALM=netcommerce
export KEYCLOAK_CLIENT_ID=netcommerce-web
export PUBLIC_ORIGIN=http://localhost:3000

./node_modules/.bin/next dev --port 3000 > /tmp/next-dev.log 2>&1 &
DEV_PID=$!
trap 'kill $DEV_PID 2>/dev/null' EXIT

# Refuse to start if the port is already taken.
#
# A stale dev server from an earlier run keeps the port AND leaves a "Ready in"
# line in a previous log, so the wait below would succeed against a process that
# is not this script's — while the server it actually started had already exited
# on EADDRINUSE. That produced a confusing ECONNREFUSED from an unrelated PID.
# Failing here names the real problem instead.
if command -v netstat >/dev/null 2>&1 && netstat -ano 2>/dev/null | grep -q ':3000 .*LISTENING'; then
  echo "ABORT: port 3000 is already in use by another process."
  echo "       Stop it first, or the Next started below will exit on EADDRINUSE"
  echo "       and the readiness wait will match a stranger's log line."
  netstat -ano 2>/dev/null | grep ':3000 .*LISTENING'
  exit 1
fi

# Wait for THIS process to report ready. The PID check matters as much as the
# grep: a Next that dies on a port clash never writes "Ready in", and the loop
# would otherwise spin for 90s against a dead server.
for i in $(seq 1 90); do
  grep -q "Ready in" /tmp/next-dev.log 2>/dev/null && break
  kill -0 $DEV_PID 2>/dev/null || { echo "DEV EXITED"; cat /tmp/next-dev.log; exit 1; }
  sleep 1
done

grep -q "Ready in" /tmp/next-dev.log 2>/dev/null || {
  echo "DEV NEVER BECAME READY"
  cat /tmp/next-dev.log
  exit 1
}

echo "=== server up ==="
grep -E "Local:|Ready in" /tmp/next-dev.log
echo

node scripts/check-render.mjs
STATUS=$?

if [ $STATUS -ne 0 ]; then
  echo
  echo "########## DIAGNOSTIC: locate banned-font mentions ##########"
  node scripts/locate-font-mentions.mjs
fi

kill $DEV_PID 2>/dev/null
exit $STATUS