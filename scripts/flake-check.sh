#!/usr/bin/env bash
# Is the remaining failure deterministic or a flake? Three consecutive runs.
# A race-condition test that fails 3/3 is a real bug; 1/3 is flakiness and
# needs a different fix (tuning, not logic).
set -uo pipefail
cd "$(dirname "$0")/.."
source scripts/test-env.sh

for i in 1 2 3; do
  printf 'run %s: ' "$i"
  dotnet test tests/NetCommerce.Integration.Tests/NetCommerce.Integration.Tests.csproj \
    --nologo -c Release --filter "FullyQualifiedName~ConcurrentOrderAndWebhook" 2>&1 \
    | grep -aoE '(Failed|Passed)! *- Failed: *[0-9]+, Passed: *[0-9]+' | head -1
done