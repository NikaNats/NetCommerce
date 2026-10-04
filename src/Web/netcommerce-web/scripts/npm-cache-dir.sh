#!/usr/bin/env bash
# Resolve a writable npm cache directory, and create it.
#
# WHY THIS EXISTS
#
# These scripts run in two very different places:
#   - the local sandbox, where /scratch is a writable scratch root
#   - GitHub Actions, where /scratch does not exist and cannot be created
#     (mkdir fails: "Permission denied", and `set -e` then kills the step)
#
# A hardcoded `export npm_config_cache=/scratch/.npm` plus `mkdir -p /scratch/.npm`
# therefore worked locally and broke CI. check.sh had already been fixed for this;
# check-headers.sh and verify-render.sh had not, so the failure surfaced only when
# the frontend gate ran on a real runner.
#
# Sourced rather than executed: the caller needs the exported variable in its own
# shell, and a subshell would discard it.
#
# Usage:  source "$(dirname "$0")/npm-cache-dir.sh"

# The sandbox scratch root. Overridable so a test can simulate a machine where
# it does not exist (CI), without needing a second host.
: "${SCRATCH_ROOT:=/scratch}"

# Prefer an explicitly provided cache (CI may set one).
if [ -n "${npm_config_cache:-}" ]; then
  mkdir -p "$npm_config_cache" 2>/dev/null || true
  :
elif [ -d "$SCRATCH_ROOT" ] && [ -w "$SCRATCH_ROOT" ]; then
  # Local sandbox.
  export npm_config_cache="$SCRATCH_ROOT/.npm"
  mkdir -p "$npm_config_cache"
else
  # Anywhere else, including GitHub Actions. Project-local so the path is always
  # writable and never collides with another checkout.
  export npm_config_cache="${PWD}/.npm-cache"
  mkdir -p "$npm_config_cache"
fi

export npm_config_cache