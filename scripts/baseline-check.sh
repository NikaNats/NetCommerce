#!/usr/bin/env bash
# Baseline vs. current comparison for the one remaining integration failure.
#
# Answers a specific question: is ConcurrentOrderAndWebhook_ShouldHandleGracefully
# broken by MY changes, or was it already failing? Stashing isolates that.
set -uo pipefail
cd "$(dirname "$0")/.."
source scripts/test-env.sh

FILTER="${1:-FullyQualifiedName~ConcurrentOrderAndWebhook}"

run() {
  dotnet test tests/NetCommerce.Integration.Tests/NetCommerce.Integration.Tests.csproj \
    --nologo -c Release --filter "$FILTER" 2>&1 | grep -aE 'Failed:|Passed:' | head -2
}

echo "=== WITH my changes ==="
run

# Capture the baseline to a FILE, not a grep. The previous version printed
# nothing for the baseline and I read that as "no result" — it was actually a
# COMPILE FAILURE. Those summary lines only exist if a test run happened, so an
# empty grep is ambiguous where a log file is not.
echo "=== WITHOUT my changes (stashed) ==="
git stash -q
dotnet test tests/NetCommerce.Integration.Tests/NetCommerce.Integration.Tests.csproj \
  --nologo -c Release --filter "$FILTER" > .baseline.log 2>&1
echo "dotnet test exit: $?"
git stash pop -q
echo "(changes restored)"
echo "=== compile errors in the baseline? ==="
grep -aoE 'error CS[0-9]+' .baseline.log | sort | uniq -c | sort -rn | head -5
echo "=== did any test actually run? ==="
grep -aE 'Failed:|Passed:' .baseline.log | head -3 || echo "  NO TEST SUMMARY - build never produced a test run"