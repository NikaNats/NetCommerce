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

for i in $(seq 1 90); do
  grep -q "Ready in" /tmp/next-dev.log 2>/dev/null && break
  kill -0 $DEV_PID 2>/dev/null || { echo "DEV EXITED"; cat /tmp/next-dev.log; exit 1; }
  sleep 1
done

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