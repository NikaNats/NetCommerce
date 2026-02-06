-- =====================================================================
-- Phase 6 Post-Migration: Audit Log Namespace Migration (Optional)
-- =====================================================================
-- Purpose: Update historical audit log entries that reference legacy
--          SharedKernel namespaces to use the new canonical Domain.Shared
--          namespaces.
--
-- WHEN TO RUN: After verifying no active sagas/outbox messages use legacy types.
--
-- CRITICAL: Test on a staging environment first. This modifies historical data.
-- =====================================================================

-- Backup the audit_logs table before running this migration
-- CREATE TABLE audit_logs_backup_20260204 AS SELECT * FROM public.audit_logs;

BEGIN;

-- =====================================================================
-- 1. Show the scope of the migration
-- =====================================================================
SELECT
    'Audit Logs with Legacy Types' as category,
    COUNT(*) as total_affected
FROM public.audit_logs
WHERE context::text LIKE '%NetCommerce.SharedKernel%';

-- =====================================================================
-- 2. Preview the changes (first 5 records)
-- =====================================================================
SELECT
    id,
    aggregate_type,
    command_type,
    LEFT(context::text, 100) as context_preview_before,
    LEFT(
        REPLACE(context::text, 'NetCommerce.SharedKernel', 'NetCommerce.Domain.Shared'),
        100
    ) as context_preview_after
FROM public.audit_logs
WHERE context::text LIKE '%NetCommerce.SharedKernel%'
ORDER BY timestamp DESC
LIMIT 5;

-- =====================================================================
-- 3. Perform the namespace migration
-- =====================================================================

-- Update Money type references
UPDATE public.audit_logs
SET context = REPLACE(context::text, 'NetCommerce.SharedKernel.Domain.Money', 'NetCommerce.Domain.Shared.Money')::jsonb
WHERE context::text LIKE '%NetCommerce.SharedKernel.Domain.Money%';

-- Update PriceBreakdown type references
UPDATE public.audit_logs
SET context = REPLACE(context::text, 'NetCommerce.SharedKernel.Domain.PriceBreakdown', 'NetCommerce.Domain.Shared.PriceBreakdown')::jsonb
WHERE context::text LIKE '%NetCommerce.SharedKernel.Domain.PriceBreakdown%';

-- Update Integration Event references
UPDATE public.audit_logs
SET context = REPLACE(context::text, 'NetCommerce.SharedKernel.Events', 'NetCommerce.Domain.Shared.Events')::jsonb
WHERE context::text LIKE '%NetCommerce.SharedKernel.Events%';

-- Catch-all for any remaining SharedKernel references
UPDATE public.audit_logs
SET context = REPLACE(context::text, 'NetCommerce.SharedKernel', 'NetCommerce.Domain.Shared')::jsonb
WHERE context::text LIKE '%NetCommerce.SharedKernel%';

-- =====================================================================
-- 4. Verify the migration
-- =====================================================================
SELECT
    'Remaining Legacy References' as verification,
    COUNT(*) as should_be_zero
FROM public.audit_logs
WHERE context::text LIKE '%NetCommerce.SharedKernel%';

-- If should_be_zero = 0, migration is complete. COMMIT.
-- If should_be_zero > 0, investigate the remaining records. ROLLBACK.

-- =====================================================================
-- 5. Statistics after migration
-- =====================================================================
SELECT
    'Canonical Type References' as category,
    COUNT(*) as total_canonical
FROM public.audit_logs
WHERE context::text LIKE '%NetCommerce.Domain.Shared%';

-- COMMIT; -- Uncomment to apply the changes
-- ROLLBACK; -- Uncomment to undo the changes
