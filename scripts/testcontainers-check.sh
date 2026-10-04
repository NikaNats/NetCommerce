#!/usr/bin/env bash
# Did my env-var override break this, or is it broken regardless?
#
# IntegrationTestFixture uses Testcontainers, which provisions its own Postgres
# and Redis. Setting CONNECTIONSTRINGS__* by hand likely OVERRODE that, pointing
# the fixture at a bare database with no migrations applied — including no
# Wolverine durable-inbox tables, which would explain a message that is
# "Received" but never "Handled".
#
# So: run the test with NO env override and let Testcontainers do its job.
set -uo pipefail
cd "$(dirname "$0")/.."

echo "=== NO env override — Testcontainers provisions the database ==="
env -u CONNECTIONSTRINGS__CATALOGDB \
    -u CONNECTIONSTRINGS__ORDERINGDB \
    -u CONNECTIONSTRINGS__INVENTORYDB \
    -u CONNECTIONSTRINGS__PAYMENTSDB \
    -u CONNECTIONSTRINGS__FINANCEDB \
    -u CONNECTIONSTRINGS__SHIPPINGDB \
    -u CONNECTIONSTRINGS__POSTGRES \
    -u CONNECTIONSTRINGS__REDIS \
  dotnet test tests/NetCommerce.Integration.Tests/NetCommerce.Integration.Tests.csproj \
    --nologo -c Release --filter "FullyQualifiedName~ConcurrentOrderAndWebhook" 2>&1 \
    | grep -aE 'Failed:|Passed:|Missing connection' | head -4