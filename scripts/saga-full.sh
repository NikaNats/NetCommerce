#!/usr/bin/env bash
# Full, untruncated activity table for the failing saga test.
#
# Previous greps showed only 'Sent' and 'Received' lines, and one output was
# truncated mid-table. If a handler threw, Wolverine logs it near the envelope —
# so the whole table is needed, not a filtered subset.
set -uo pipefail
cd "$(dirname "$0")/.."
source scripts/test-env.sh

dotnet test tests/NetCommerce.Integration.Tests/NetCommerce.Integration.Tests.csproj \
  --nologo -c Release --filter "FullyQualifiedName~ConcurrentOrderAndWebhook" \
  > .saga2.log 2>&1
echo "exit: $?"
echo "=== FULL activity table ==="
sed -n '/Message Type/,/^--------/p' .saga2.log | head -60