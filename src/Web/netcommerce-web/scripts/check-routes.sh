#!/usr/bin/env bash
# Boot the dev server in the sandbox and probe the storefront routes with Node.
# curl is NOT installed in the sandbox (exit 127) — that is what made an earlier
# version of this probe report a false "DEV SERVER NEVER BECAME READY".
set -Eeuo pipefail

# Directory arrives as $1: inside the sandbox a hardcoded "C:/..." is not a valid
# path, and this script must run from the sandbox that also runs the server.
cd "${1:?pass the app directory}" || exit 1

./node_modules/.bin/next dev --port 3000 > /tmp/routes-dev.log 2>&1 &
NEXT_PID=$!
trap 'kill $NEXT_PID 2>/dev/null' EXIT

# Same guard as verify-render.sh: a stale server holding :3000 makes the readiness
# wait below match a stranger's log line while this Next has already exited on
# EADDRINUSE. check.sh runs verify-render.sh first, so this catches a leftover
# from an earlier run or from a manual `next dev`.
if command -v netstat >/dev/null 2>&1 && netstat -ano 2>/dev/null | grep -q ':3000 .*LISTENING'; then
  echo "ABORT: port 3000 is already in use."
  netstat -ano 2>/dev/null | grep ':3000 .*LISTENING'
  exit 1
fi

echo "waiting for :3000"
for i in $(seq 1 90); do
  if node -e "fetch('http://127.0.0.1:3000/').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))" >/dev/null 2>&1; then
    echo "ready after ${i}s"
    break
  fi
  sleep 1
done

if ! node -e "fetch('http://127.0.0.1:3000/').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))" >/dev/null 2>&1; then
  echo "DEV SERVER NEVER BECAME READY"
  tail -30 /tmp/routes-dev.log
  exit 1
fi

# The API is intentionally NOT running: these assertions cover graceful
# degradation. A page that 500s, or claims a product "does not exist" when the
# service is merely unreachable, is a defect — and that one passed `next build`
# while failing here.
node "$(dirname "$0")/probe-routes.mjs" "http://127.0.0.1:3000"