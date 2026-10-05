#!/usr/bin/env bash
# Extract the real saga transition table: each Handle method, the message type
# that triggers it, the state it assigns, and the file:line. This is the
# evidence the lifecycle diagram must cite -- no guessing.
set -uo pipefail
cd "$(dirname "$0")/.."

echo "=== Handler methods and their triggering message types ==="
for f in src/Ordering/Ordering.Application/Sagas/OrderFulfillmentSaga.*.cs; do
  base=$(basename "$f")
  # Print the signature block of each public Handle/Start/NotFound plus the
  # message type it accepts, and any State assignment inside.
  awk -v F="$base" '
    /public (static )?.*(Handle|Start)\(/ {inblock=1; depth=0; sig=$0; start=NR}
    inblock {
      sig = sig " " $0
      opens = gsub(/\(/,"(",$0); closes = gsub(/\)/,")",$0)
      depth += opens - closes
      if (depth <= 0 && start != NR) {
        print "--- " F ":" start
        print "    SIG: " sig
        inblock=0
      }
    }
  ' "$f"
done | head -80

echo
echo "=== State assignments with file:line ==="
grep -rn 'State = OrderFulfillmentState\.' src/Ordering/Ordering.Application/Sagas/*.cs \
  | sed 's|src/Ordering/Ordering.Application/Sagas/||'

echo
echo "=== Timeout message delays (exact values) ==="
grep -n -A2 'record .*TimeoutMessage : TimeoutMessage' src/Domain.Shared/NetCommerce.Domain.Shared/Events/SagaMessages.cs