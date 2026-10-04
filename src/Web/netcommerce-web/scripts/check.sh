#!/usr/bin/env bash
# Single verification entry point: install (if needed) -> CVE guard -> typecheck
# -> tests -> production build.
#
# Run inside the docker sandbox, from src/Web/netcommerce-web:
#   SECCOMP_PROFILE=none SANDBOX_ALLOW_DIR="$PWD" SANDBOX_RW=1 NETWORK=bridge \
#     bash ~/hermes-config/scripts/run-sandbox.sh -lc "bash scripts/verify.sh"
#
# Those four variables are required on this host: the sandbox allowlist does not
# cover ~/source, the default seccomp profile file is absent, the rootfs is
# read-only, and npm needs the bridge network.
set -Eeuo pipefail

# npm cache location. Shared with check-headers.sh and verify-render.sh so all
# three behave identically in the sandbox AND on a GitHub runner, where a
# hardcoded /scratch path fails with "Permission denied".
# shellcheck source=scripts/npm-cache-dir.sh
. "$(dirname "$0")/npm-cache-dir.sh"

# --- dependency install (skipped when node_modules is already present) --------
# `npm ci` against the COMMITTED lockfile, which is what a fresh clone and CI
# both run. The previous version deleted package-lock.json and reinstalled, which
# meant the lockfile the gate verified was never the one it had just rewritten.
if [ ! -x node_modules/.bin/next ]; then
  echo "### installing dependencies from the committed lockfile"
  if [ -f package-lock.json ]; then
    npm ci --no-audit --no-fund
  else
    echo "  (no package-lock.json — installing and creating one)"
    npm install --no-audit --no-fund
  fi
fi

# --- CVE guard ---------------------------------------------------------------
# next < 16.0.7 is vulnerable to CVE-2025-66478 (CVSS 10.0, RCE via the RSC
# protocol). Fail loudly rather than shipping a known-vulnerable framework.
echo "### CVE-2025-66478 guard"
node -e "
const v = require('./node_modules/next/package.json').version;
const [maj, min, pat] = v.split('.').map(Number);
const safe = maj > 16 || (maj === 16 && (min > 0 || (min === 0 && pat >= 7)));
console.log('  next ' + v + ' -> ' + (safe ? 'PATCHED' : 'VULNERABLE'));
process.exit(safe ? 0 : 1);
"

echo
echo "### typecheck"
./node_modules/.bin/tsc --noEmit

echo
echo "### tests"
./node_modules/.bin/vitest run

echo
echo "### production build"
./node_modules/.bin/next build

echo
echo "### rendered-output design invariants"
bash scripts/verify-render.sh

echo
echo "### storefront route behaviour (API deliberately down)"
# Runs LAST: it boots a dev server, and these assertions cover graceful
# degradation. A product page that 500s when the API is unreachable, or that
# claims the item "does not exist", passed `next build` and failed here.
bash scripts/check-routes.sh "$PWD"

echo
echo "### security headers (asserted on a live response)"
# A CSP declared in next.config.ts but never served is decoration, so this reads
# the headers off a real response rather than reading the config back.
bash scripts/check-headers.sh "$PWD"

echo
echo "### npm cache resolution (sandbox + CI)"
# This gate runs in the local sandbox AND on a GitHub runner. A hardcoded
# /scratch npm cache works in one and dies in the other with
# "Permission denied" — which is exactly how the previous CI run failed, after
# 401 tests, a production build, and every render check had already passed.
bash scripts/check-npm-cache.sh

echo
echo "ALL CHECKS PASSED"
