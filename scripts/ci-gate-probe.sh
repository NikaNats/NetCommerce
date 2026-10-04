#!/usr/bin/env bash
# Prove the CI build command still catches a REAL production violation.
#
# Removing /p:TreatWarningsAsErrors could be a hollow win: if it silently
# disabled strictness everywhere, the gate would pass while catching nothing.
# So: inject a genuine analyzer violation into PRODUCTION code, confirm the CI
# command FAILS, then restore.
#
# CA1861 (constant arrays passed as arguments) is a warning-class rule promoted
# to an error in Release/CI for non-test projects.
set -uo pipefail
cd "$(dirname "$0")/.."

TARGET="src/Catalog/Application/Categories/DTOs/CategoryDto.cs"
MARKER="// __CI_GATE_PROBE__"

fail() { echo "FAIL: $*"; exit 1; }
pass() { echo "PASS: $*"; }

[ -f "$TARGET" ] || fail "probe target missing: $TARGET"
grep -q "$MARKER" "$TARGET" && fail "probe marker already present — restore first"

cp "$TARGET" "$TARGET.probe-bak"

cleanup() {
  [ -f "$TARGET.probe-bak" ] && mv "$TARGET.probe-bak" "$TARGET"
}
trap cleanup EXIT

# CA1304: culture-sensitive ToLower(). Genuine violation, warning-class, and
# promoted to error for production projects in Release.
cat >> "$TARGET" <<'PROBE'

PROBE
if ! grep -q 'ToLower()' "$TARGET"; then
  cat >> "$TARGET" <<'PROBE'
public static class __CiGateProbe
{
    public static bool IsActive(string env) => env.ToLower() == "production";
}
PROBE
fi

echo "=== injected a CA1304 violation into production code ==="
echo "=== running CI's build command: expect FAILURE ==="

out=$(CI=true dotnet build NetCommerce.slnx -c Release --nologo --no-incremental 2>&1)
if echo "$out" | grep -aqE 'error CA1304|Build FAILED'; then
  echo "$out" | grep -aoE 'error CA1304[^[]*' | head -1
  pass "gate correctly FAILED on a production analyzer violation"
else
  fail "gate PASSED despite a real violation — strictness was lost, not restored"
fi