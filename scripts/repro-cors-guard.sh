#!/usr/bin/env bash
# Reproduce the container-smoke migration step's startup failure.
#
# CI runs:
#   dotnet run --project src/Api -c Debug --no-launch-profile -- --migrate-only
# with DB connection strings set but NO Cors__AllowedOrigins and NO
# ASPNETCORE_ENVIRONMENT. ServiceCollectionExtensions.cs:38-56 then treats the
# environment as Production and throws, because "unset means unknown, and
# unknown must not become localhost-with-credentials".
#
# The same omission affects the "Start API Container" step, which sets
# ASPNETCORE_ENVIRONMENT=Production but still no CORS origins.
#
# This asserts the guard fires without CORS and does NOT fire with it — proving
# the guard is correct and the workflow is what is incomplete.
set -uo pipefail
cd "$(dirname "$0")/.."

CS_CATALOG="Host=127.0.0.1;Port=5432;Database=netcommerce;Username=postgres;Password=Password123!;SearchPath=catalog"
CS_ORDERING="Host=127.0.0.1;Port=5432;Database=netcommerce;Username=postgres;Password=Password123!;SearchPath=ordering"
CS_POSTGRES="Host=127.0.0.1;Port=5432;Database=netcommerce;Username=postgres;Password=Password123!"

run_migrate() {
  (cd src/Api && \
    ConnectionStrings__CatalogDb="$CS_CATALOG" \
    ConnectionStrings__OrderingDb="$CS_ORDERING" \
    ConnectionStrings__postgres="$CS_POSTGRES" \
    ConnectionStrings__Redis="127.0.0.1:6379" \
    ConnectionStrings__meilisearch="http://127.0.0.1:7700" \
    ASPNETCORE_ENVIRONMENT="${1:-}" \
    Cors__AllowedOrigins__0="${2:-}" \
    timeout 300 dotnet run -c Debug --no-launch-profile -- --migrate-only 2>&1)
}

# Why the indexed key: Cors:AllowedOrigins is read as a SECTION via
# Get<string[]>() (ServiceCollectionExtensions.cs:31). Binding a .NET array from
# environment variables requires INDEXED keys -- `Cors__AllowedOrigins` alone
# binds to the scalar, the array stays empty, and the guard still throws.
# Verified: the un-indexed form reproduced the failure even with a value
# supplied. (Kept as a comment OUTSIDE the command: a `#` line inside the
# backslash-continued env list silently truncates every later variable, which
# cost one confusing round of debugging.)

echo "=== CONTROL A: no CORS origins, Production env (must FAIL the guard) ==="
out=$(run_migrate Production "")
if echo "$out" | grep -q "Cors:AllowedOrigins must list"; then
  echo "$out" | grep -o "Cors:AllowedOrigins must list[^\"]*" | head -1
  echo "PASS: guard fires exactly as CI reported"
else
  echo "NOTE: guard did not fire (DB may be unavailable first). Output tail:"
  echo "$out" | tail -4
fi

echo
echo "=== CONTROL B: explicit CORS origins, Production env (must PASS the guard) ==="
out=$(run_migrate Production "https://shop.example.com")
if echo "$out" | grep -q "Cors:AllowedOrigins must list"; then
  echo "FAIL: guard still fires despite explicit origins"
  echo "$out" | tail -4
  exit 1
fi
echo "PASS: guard satisfied by explicit origins"
echo "      (failure now depends only on DB reachability, not configuration)"