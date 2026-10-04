#!/usr/bin/env bash
# Capture the full failure detail for the saga hang, including which cascaded
# message never completed. Previous greps truncated exactly the lines that
# identify the culprit.
set -uo pipefail
cd "$(dirname "$0")/.."
source scripts/test-env.sh

dotnet test tests/NetCommerce.Integration.Tests/NetCommerce.Integration.Tests.csproj \
  --nologo -c Release --filter "FullyQualifiedName~ConcurrentOrderAndWebhook" \
  > .saga.log 2>&1
echo "exit: $?"
echo "=== the failure block ==="
sed -n '/ConcurrentOrderAndWebhook_ShouldHandleGracefully \[FAIL\]/,/^Failed!/p' .saga.log | head -40