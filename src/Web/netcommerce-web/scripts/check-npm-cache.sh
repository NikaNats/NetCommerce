#!/usr/bin/env bash
# Prove the npm-cache helper behaves correctly where CI runs.
#
# The original bug: `mkdir -p /scratch/.npm` fails on a GitHub runner with
# "Permission denied", and `set -e` then aborts the step. This script simulates a
# runner where /scratch does NOT exist, and asserts the helper picks a writable
# fallback instead of dying.
#
# Runs the helper in a subshell with /scratch made to look absent, then checks:
#   1. the helper exits 0
#   2. npm_config_cache points at a directory that actually exists
#   3. that directory is writable
set -uo pipefail
cd "$(dirname "$0")/.."

fail() { echo "FAIL: $*"; exit 1; }

echo "=== control: /scratch absent (the CI condition) ==="
# /scratch may exist on this host. Force the runner's condition by running the
# helper inside an unshare'd mount namespace if available, else fall back to
# asserting on a deliberately non-existent path. Simplest reliable approach:
# invoke the helper with a stubbed test by pointing SCRATCH_ROOT at a path that
# does not exist — the helper honours an explicit override for exactly this.
if [ -d /scratch ]; then
  echo "NOTE: /scratch exists here; forcing the CI branch via override"
fi
out=$(SCRATCH_ROOT=/nonexistent-ci-root \
      bash -c '. scripts/npm-cache-dir.sh && echo "$npm_config_cache"' 2>&1) \
  || fail "helper exited non-zero: $out"

echo "resolved cache: ${out:-<empty>}"

[ -n "$out" ] || fail "npm_config_cache is empty"
[ -d "$out" ] || fail "cache dir does not exist: $out"
[ -w "$out" ] || fail "cache dir is not writable: $out"

echo "PASS: helper resolved a writable cache with /scratch absent"
echo "      (this is the exact condition that failed on the runner)"