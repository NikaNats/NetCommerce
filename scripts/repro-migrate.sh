#!/usr/bin/env bash
# Run the container-smoke migration step for real: with the exact env CI now
# sets (CORS satisfied, Production) against a real PostgreSQL, and assert the
# module schemas actually get created.
#
# This is the step that failed in CI. Control B only proved the GUARD stopped
# firing; this proves the step's actual job — applying 6 module schemas — works.
set -uo pipefail
cd "$(dirname "$0")/.."

SCHEMA_LIST="catalog ordering inventory payments finance shipping"
fail() { echo "FAIL: $*"; exit 1; }

# Ensure the database is reachable before blaming configuration.
if ! docker exec nc-corspg pg_isready -U postgres >/dev/null 2>&1; then
  echo "SKIP: nc-corspg is not running; start it to exercise the migration"
  exit 2
fi

echo "=== resetting target schemas ==="
docker exec nc-corspg psql -U postgres -d netcommerce -q -c \
  "drop schema if exists catalog cascade; drop schema if exists ordering cascade;
   drop schema if exists inventory cascade; drop schema if exists payments cascade;
   drop schema if exists finance cascade; drop schema if exists shipping cascade;" \
  >/dev/null 2>&1

echo "=== running: dotnet run -- --migrate-only (Production, CORS satisfied) ==="
out=$(cd src/Api && \
  ConnectionStrings__CatalogDb="Host=127.0.0.1;Port=5432;Database=netcommerce;Username=postgres;Password=Password123!;SearchPath=catalog" \
  ConnectionStrings__OrderingDb="Host=127.0.0.1;Port=5432;Database=netcommerce;Username=postgres;Password=Password123!;SearchPath=ordering" \
  ConnectionStrings__InventoryDb="Host=127.0.0.1;Port=5432;Database=netcommerce;Username=postgres;Password=Password123!;SearchPath=inventory" \
  ConnectionStrings__PaymentsDb="Host=127.0.0.1;Port=5432;Database=netcommerce;Username=postgres;Password=Password123!;SearchPath=payments" \
  ConnectionStrings__FinanceDb="Host=127.0.0.1;Port=5432;Database=netcommerce;Username=postgres;Password=Password123!;SearchPath=finance" \
  ConnectionStrings__ShippingDb="Host=127.0.0.1;Port=5432;Database=netcommerce;Username=postgres;Password=Password123!;SearchPath=shipping" \
  ConnectionStrings__postgres="Host=127.0.0.1;Port=5432;Database=netcommerce;Username=postgres;Password=Password123!" \
  ConnectionStrings__Redis="127.0.0.1:6379" \
  ConnectionStrings__meilisearch="http://127.0.0.1:7700" \
  ASPNETCORE_ENVIRONMENT=Production \
  Cors__AllowedOrigins__0="https://shop.example.com" \
  timeout 600 dotnet run -c Debug --no-launch-profile -- --migrate-only 2>&1)
code=$?

echo "--- last 12 lines ---"
echo "$out" | tail -12

if echo "$out" | grep -q "Cors:AllowedOrigins must list"; then
  fail "CORS guard fired despite configured origins"
fi

if [ $code -ne 0 ]; then
  fail "migrate-only exited $code"
fi

echo
echo "=== schemas and table counts after migration ==="
missing=0
for s in $SCHEMA_LIST; do
  n=$(docker exec nc-corspg psql -U postgres -d netcommerce -tAc \
    "select count(*) from information_schema.tables where table_schema='$s'" 2>/dev/null | tr -d '[:space:]')
  if [ -z "$n" ] || [ "$n" = "0" ]; then
    echo "  $s: NO TABLES"
    missing=1
  else
    echo "  $s: $n tables"
  fi
done

[ $missing -eq 0 ] || fail "one or more module schemas were not created"

echo
echo "PASS: all 6 module schemas migrated under Production + configured CORS"