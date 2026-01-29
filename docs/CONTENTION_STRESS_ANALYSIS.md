# Contention-Specific Stress Analysis Guide

## Overview

This document describes how to run ACM-grade contention stress tests for the NetCommerce Partitioned Sequential Messaging architecture.

## Test Categories

### 1. Single-Key Saturation ("Zero-Lock" Benchmark)

**File:** `ContentionStressTests.cs` - `SingleKeySaturation_5000Requests_SamProductId_ShouldHaveZeroDeadlocks`

**Theory:** When 5,000 requests target the SAME ProductId, Wolverine routes them all to ONE partition slot (of 9). This creates Head-of-Line blocking but ZERO database deadlocks.

**Metrics to Watch:**
| Metric | Target | Meaning |
|--------|--------|---------|
| Deadlock Rate | 0.00% | Proves partitioning removed DB contention |
| Lock Timeout Errors | 0 | No FOR UPDATE deadlocks |
| Linearity Ratio (P99/P50) | < 10.0 | Queue depth is predictable |

**Run:**
```powershell
dotnet test tests/NetCommerce.LoadTests --filter "SingleKeySaturation" --no-build
```

### 2. Partition Skew & Thread Starvation

**File:** `ContentionStressTests.cs` - `PartitionSkew_HotAndColdProducts_SameSlot_ShouldMeasureStarvation`

**Theory:** With 9 partition slots, if PS5 (hot) and Toaster (cold) hash to the same slot, the Toaster experiences "Head-of-Line" blocking behind the PS5 queue.

**What to Look For:**
- Cold product P99 latency
- If Toaster takes 5+ seconds, you have Partition Skew
- Consider increasing `PartitionSlots` or category-based partitioning

**Run:**
```powershell
dotnet test tests/NetCommerce.LoadTests --filter "PartitionSkew" --no-build
```

### 3. WAL Exhaustion (IOPS Ceiling Test)

**File:** `ContentionStressTests.cs` - `WalExhaustion_HighWriteLoad_ShouldApplyBackpressure`

**Theory:** Even with zero app-level locks, PostgreSQL's Write-Ahead Log (WAL) is a serial bottleneck. Every reservation must fsync to disk.

**What to Look For:**
- Monitor `processing_payment_count` via `SagaMonitorService`
- When saga count climbs but commits/sec plateaus = IOPS ceiling hit
- System should slow down (backpressure), NOT crash

**Run:**
```powershell
dotnet test tests/NetCommerce.LoadTests --filter "WalExhaustion" --no-build
```

### 4. Stale Cache Race (Meilisearch vs Postgres)

**File:** `ContentionStressTests.cs` - `StaleCacheRace_RapidPriceUpdates_ShouldEnforceTriplePassPricing`

**Theory:** `ProductSearchProjectionHandler` updates Meilisearch asynchronously. During high contention, search index price may lag behind Postgres.

**Invariant:** CreateOrderHandler's Triple-Pass Pricing MUST catch price changes. Users should get `409 Conflict`, NOT buy at stale price.

**Run:**
```powershell
dotnet test tests/NetCommerce.LoadTests --filter "StaleCacheRace" --no-build
```

## Prerequisites

### 1. Start the API
```powershell
dotnet run --project src/NetCommerce.AppHost/NetCommerce.AppHost.csproj
```

### 2. Verify Infrastructure
- PostgreSQL: Running on configured port
- Redis: Running (for distributed cache)
- Meilisearch: Running (for stale cache tests)
- Seq: Running (for log analysis)

### 3. Create Test Data
For stale cache tests, ensure a product exists:
```sql
INSERT INTO catalog.products (id, sku, name, price_amount, price_currency, status)
VALUES ('00000000-0000-0000-0000-000000000001', 'TEST-PRODUCT', 'Test Product', 499.99, 'GEL', 'Published');

INSERT INTO inventory.stocks (product_id, sku, quantity, reserved_quantity)
VALUES ('00000000-0000-0000-0000-000000000001', 'TEST-PRODUCT', 10000, 0);
```

## Expert Recommendations

### Warm-up Phase
All tests include a **30-second warm-up** to:
- Complete JIT compilation
- Prime connection pools
- Warm DekCache (encrypted PII)
- Stabilize hardware caches

**Warning:** Results in the first 5 seconds are often "False Negatives" due to cold start.

### Monitoring Dashboard

During tests, monitor these Prometheus/Grafana metrics:

```promql
# Active Sagas (should drain to 0 after test)
ordering_fulfillment_sagas_active

# Database Connection Pool
pg_stat_activity_count

# Wolverine Queue Depth
wolverine_queues_local_inventory_contention_count

# PostgreSQL WAL Write Rate
pg_stat_bgwriter_buffers_backend
```

### Test Report Location

NBomber generates detailed reports at:
```
./load-test-reports/
├── single-key-saturation/
├── partition-skew/
├── wal-stress/
└── stale-cache-race/
```

## Interpreting Results

### ✅ Scale-Ready (All PASS)
- Deadlock Rate: 0.00%
- Lock Timeout Errors: 0
- Linearity Ratio: < 10.0
- Saga Leak Rate: 0
- Stale Price Successes: 0

### ⚠️ Needs Attention
- Linearity Ratio 10-20: Minor contention leakage
- Cold Product P99 > 3s: Consider more partition slots
- Saga backlog not draining: Check compensating actions

### ❌ Architecture Issue
- Deadlock Rate > 0: Partitioning misconfigured
- Stale Price Successes > 0: Triple-Pass Pricing broken
- System crash under WAL stress: Backpressure mechanism failed

## Troubleshooting

### Deadlocks Detected
1. Verify all inventory handlers have `[LocalQueue("inventory-contention")]`
2. Check that `MessagePartitioning.ByMessage<T>` is configured
3. Ensure partition key matches (OrderId vs ProductId)

### High Linearity Ratio
1. Check for database locks in handlers
2. Verify no additional `FOR UPDATE` queries
3. Look for external service calls in partition path

### Saga Leaks
1. Check `ManualInterventionRequired` state count
2. Verify compensating actions (ReleaseInventoryReservation) work
3. Look for stuck state transitions in logs

## Architecture Notes

The Partitioned Sequential Messaging pattern works by:

1. **Message Partitioning**: All requests for ProductA go to same "track"
2. **Sequential Processing**: One thread processes that track
3. **No DB Locks**: Since only one thread accesses ProductA, no locks needed
4. **Predictable Latency**: Queue depth × time_per_request = latency

This converts a "Hardware Problem" (DB contention) into a "Software Problem" (queue scheduling).

---

**If NetCommerce passes all these tests, it is officially "Scale-Ready."**
