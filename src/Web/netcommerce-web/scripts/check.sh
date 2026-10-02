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

export npm_config_cache=/scratch/.npm
mkdir -p /scratch/.npm

# --- dependency install (skipped when node_modules is already present) --------
if [ ! -x node_modules/.bin/next ]; then
  echo "### installing dependencies (clean)"
  rm -rf node_modules package-lock.json
  npm install --no-audit --no-fund
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
echo "ALL CHECKS PASSED"
