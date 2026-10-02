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