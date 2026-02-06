-- =====================================================================
-- Phase 5/6 Migration Cleanup: Database Audit for Legacy Types
-- =====================================================================
-- Purpose: Verify that NO legacy SharedKernel type references exist
--          in the Wolverine persistence layer before removing
--          LegacyTypeResolver.
--
-- CRITICAL: If any query returns COUNT > 0, DO NOT proceed with purge.
-- =====================================================================

-- 1. Check for legacy types in active Saga states
SELECT COUNT(*) as legacy_saga_count
FROM wolverine.saga_state
WHERE state::text LIKE '%NetCommerce.SharedKernel%';

-- 2. Check for legacy types in Outbox messages awaiting delivery
SELECT COUNT(*) as legacy_outbox_count
FROM wolverine.wolverine_outgoing_envelopes
WHERE message_type LIKE '%NetCommerce.SharedKernel%';

-- 3. Check for legacy types in Inbox messages (idempotency logs)
SELECT COUNT(*) as legacy_inbox_count
FROM wolverine.wolverine_incoming_envelopes
WHERE message_type LIKE '%NetCommerce.SharedKernel%';

-- =====================================================================
-- Detailed Inspection (if counts > 0)
-- =====================================================================

-- Show specific saga instances with legacy types
SELECT
    id,
    saga_type,
    LEFT(state::text, 200) as state_preview,
    created_at
FROM wolverine.saga_state
WHERE state::text LIKE '%NetCommerce.SharedKernel%'
ORDER BY created_at DESC
LIMIT 10;

-- Show specific outbox messages with legacy types
SELECT
    id,
    message_type,
    destination,
    attempts,
    LEFT(body::text, 200) as body_preview
FROM wolverine.wolverine_outgoing_envelopes
WHERE message_type LIKE '%NetCommerce.SharedKernel%'
ORDER BY attempts DESC
LIMIT 10;

-- =====================================================================
-- Safe Harbor Metrics
-- =====================================================================
-- Show how many V2 (canonical) types are in use
SELECT
    'Saga State' as table_name,
    COUNT(*) as total_records,
    SUM(CASE WHEN state::text LIKE '%NetCommerce.Domain.Shared%' THEN 1 ELSE 0 END) as canonical_count,
    SUM(CASE WHEN state::text LIKE '%NetCommerce.SharedKernel%' THEN 1 ELSE 0 END) as legacy_count
FROM wolverine.saga_state

UNION ALL

SELECT
    'Outbox Envelopes',
    COUNT(*),
    SUM(CASE WHEN message_type LIKE '%NetCommerce.Domain.Shared%' THEN 1 ELSE 0 END),
    SUM(CASE WHEN message_type LIKE '%NetCommerce.SharedKernel%' THEN 1 ELSE 0 END)
FROM wolverine.wolverine_outgoing_envelopes;
