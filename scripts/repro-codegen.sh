#!/usr/bin/env bash
# Reproduce the Docker build's failing step locally, and verify the fix.
#
# src/Api/Dockerfile step 10/11 runs:
#     RUN dotnet run -- codegen write
# with NO Meilisearch connection string and no running services. It crashed with
# exit 134 (SIGABRT) because HostApplicationBuilder.Build() validates the whole
# DI graph, and SearchIndexRebuildService could not get MeilisearchClient.
#
# This runs the same command with a deliberately empty ConnectionStrings section
# so the Meilisearch branch is NOT taken — the exact CI condition.
#
# Run from the repo root.
set -uo pipefail
cd "$(dirname "$0")/.."

API_DIR="src/Api"

echo "=== confirm no meilisearch connection string is configured ==="
if grep -q '"meilisearch"' src/Api/appsettings.json 2>/dev/null; then
  echo "NOTE: appsettings.json defines meilisearch; overriding with an empty value"
fi

echo "=== running: dotnet run --project src/Api -- codegen write ==="
echo "=== (Meilisearch cleared; DB connection strings supplied) ==="

# The Docker build has appsettings.json + a base ConnectionStrings section but
# NO Meilisearch. Without a database connection string the run fails EARLIER, on
# Wolverine's outbox check, and never reaches the DI validation that actually
# broke the image — so supply one to reproduce the real failure point.
out=$(cd "$API_DIR" && \
  ConnectionStrings__meilisearch="" \
  ConnectionStrings__CatalogDb="Host=127.0.0.1;Port=5432;Database=netcommerce;Username=postgres;Password=Password123!;SearchPath=catalog" \
  ConnectionStrings__OrderingDb="Host=127.0.0.1;Port=5432;Database=netcommerce;Username=postgres;Password=Password123!;SearchPath=ordering" \
  ConnectionStrings__postgres="Host=127.0.0.1;Port=5432;Database=netcommerce;Username=postgres;Password=Password123!" \
  ASPNETCORE_ENVIRONMENT=Development \
  timeout 600 dotnet run --no-launch-profile -- codegen write 2>&1)
code=$?

echo "--- last 25 lines ---"
echo "$out" | tail -25

echo
if [ $code -eq 0 ]; then
  echo "PASS: codegen write succeeded with Meilisearch unconfigured (exit 0)"
else
  echo "FAIL: codegen write exited $code (Docker saw 134)"
  echo "$out" | grep -aiE 'MeilisearchClient|AggregateException|not able to be constructed' | head -5
  exit 1
fi