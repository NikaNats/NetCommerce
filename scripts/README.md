# NetCommerce Operations Scripts

## 🎯 Purpose

This directory contains production-ready scripts for database maintenance, verification, and deployment support.

---

## 📜 Available Scripts

### Phase 6 Migration Support

#### `Audit-LegacyTypes.sql`
**Purpose:** Verify that no legacy SharedKernel type references exist in Wolverine persistence tables before removing legacy type resolution infrastructure.

**When to Use:**
- Before deploying Phase 6 purge changes
- As part of production deployment checklist
- During post-migration verification

**Usage:**
```bash
# PostgreSQL
psql -U username -d netcommerce -f Audit-LegacyTypes.sql

# Docker
docker exec -i netcommerce-postgres psql -U test -d netcommerce < Audit-LegacyTypes.sql
```

**Expected Results:**
```
legacy_saga_count     | 0
legacy_outbox_count   | 0
legacy_inbox_count    | 0
```

**⚠️ CRITICAL:** If ANY count > 0, DO NOT deploy Phase 6. Wait for saga completion or implement type forwarding.

**Reference:** [PHASE_6_PURGE_COMPLETE.md](../docs/PHASE_6_PURGE_COMPLETE.md)

---

#### `Migrate-AuditLogs-Namespaces.sql`
**Purpose:** Update historical audit log entries to use canonical Domain.Shared namespaces instead of legacy SharedKernel namespaces.

**When to Use:**
- After successful Phase 6 deployment
- Optional (only if audit viewer requires strict deserialization)
- Test on staging first

**Usage:**
```bash
# PostgreSQL (Transaction mode - review before COMMIT)
psql -U username -d netcommerce -f Migrate-AuditLogs-Namespaces.sql

# Script includes:
# - Preview queries
# - Backup reminder
# - ROLLBACK/COMMIT decision point
```

**⚠️ BACKUP REQUIRED:**
```sql
CREATE TABLE audit_logs_backup_20260204 AS SELECT * FROM public.audit_logs;
```

**Reference:** [PHASE_6_PURGE_COMPLETE.md](../docs/PHASE_6_PURGE_COMPLETE.md#optional-audit-log-migration)

---

### Native AOT Verification

#### `Verify-NativeAOT.ps1`
**Purpose:** 5-checkpoint verification protocol for Native AOT build readiness.

**Checkpoints:**
1. **Build Warnings** - IL2026/IL3050 analysis
2. **Binary Size** - Target < 85 MB
3. **Startup Time** - Target < 400ms cold start
4. **Wolverine Source Generation** - Handler pre-compilation
5. **Functional Test** - Order creation endpoint

**Usage:**
```powershell
# Run all checkpoints
.\Verify-NativeAOT.ps1

# Run specific checkpoints
.\Verify-NativeAOT.ps1 -CheckpointsToRun "1,4,5"

# Skip Docker build (use existing image)
.\Verify-NativeAOT.ps1 -SkipBuild

# Custom database connection
.\Verify-NativeAOT.ps1 -DatabaseConnectionString "Host=prod-db;Database=netcommerce;..."
```

**Expected Output:**
```
✅ PASS: Zero IL2026 warnings in critical path
✅ PASS: Binary size 84.1 MB (target: < 85 MB)
✅ PASS: Startup time 380ms (target: < 400ms)
✅ PASS: 247 Wolverine handlers source-generated
✅ PASS: Order creation functional test succeeded
```

**Reference:** [NATIVE_AOT_VERIFICATION.md](../docs/NATIVE_AOT_VERIFICATION.md)

---

## 🚀 Deployment Workflow

### Pre-Deployment Checklist

```powershell
# 1. Database Audit (MUST return zeros)
psql -U prod_user -d netcommerce -f Audit-LegacyTypes.sql

# 2. Native AOT Verification (MUST pass all checks)
.\Verify-NativeAOT.ps1 -CheckpointsToRun "1,4,5"

# 3. Review deployment plan
# See: docs/PHASE_6_PURGE_QUICK_REF.md
```

### Post-Deployment Verification

```powershell
# 1. Functional test
curl -X POST https://api.netcommerce.local/api/v1/orders `
  -H "Content-Type: application/json" `
  -d '{"items":[{"productId":"test","quantity":1}],"totalAmount":{"amount":100,"currency":"GEL"}}'

# 2. Monitor logs for errors
docker logs netcommerce-api | grep "JsonException"

# 3. (Optional) Migrate audit logs
psql -U prod_user -d netcommerce -f Migrate-AuditLogs-Namespaces.sql
```

---

## 🛡️ Emergency Procedures

### Rollback Scenario: "Ghost" Saga Detected

**Symptom:**
```
JsonException: Could not load type 'NetCommerce.SharedKernel.Domain.Money'
```

**Action:**
1. **Immediate Rollback** - Restore previous deployment
2. **Identify Affected Sagas:**
   ```sql
   SELECT id, saga_type, state
   FROM wolverine.saga_state
   WHERE state::text LIKE '%SharedKernel%';
   ```
3. **File Incident Report** - Include affected saga IDs
4. **Wait for TTL** - 30-day business cycle for saga completion
5. **Re-audit and Re-deploy**

**Reference:** [PHASE_6_PURGE_QUICK_REF.md](../docs/PHASE_6_PURGE_QUICK_REF.md#emergency-rollback)

---

## 📊 Script Output Examples

### ✅ Successful Audit (Clear for Deployment)

```
legacy_saga_count     | 0
legacy_outbox_count   | 0
legacy_inbox_count    | 0

Canonical Type References
canonical_count       | 247
```

**Action:** Proceed with Phase 6 deployment ✅

### ❌ Failed Audit (Migration Incomplete)

```
legacy_saga_count     | 12
legacy_outbox_count   | 3
legacy_inbox_count    | 45

Oldest Legacy Saga
id                    | abc-123-def
saga_type             | OrderFulfillmentSaga
created_at            | 2026-01-15 10:23:45
```

**Action:** ⛔ DO NOT DEPLOY. Wait for saga completion or implement type forwarding.

---

## 🔧 Troubleshooting

### Issue: psql command not found

**Solution:**
```powershell
# Windows (add to PATH)
$env:PATH += ";C:\Program Files\PostgreSQL\16\bin"

# Or use Docker
docker exec -i netcommerce-postgres psql -U test -d netcommerce
```

### Issue: Permission denied on audit_logs table

**Solution:**
```sql
-- Grant necessary permissions
GRANT SELECT, UPDATE ON public.audit_logs TO deployment_user;
```

### Issue: Verify-NativeAOT.ps1 fails on Checkpoint 2

**Problem:** Binary size exceeds 85 MB

**Solution:**
1. Review unused dependencies: `dotnet list package --include-transitive`
2. Ensure PublishTrimmed=true in project file
3. Check for accidental reflection usage (Checkpoint 1)

---

## 📚 Documentation Reference

| Script | Documentation |
|--------|---------------|
| `Audit-LegacyTypes.sql` | [PHASE_6_PURGE_COMPLETE.md](../docs/PHASE_6_PURGE_COMPLETE.md) |
| `Migrate-AuditLogs-Namespaces.sql` | [PHASE_6_PURGE_COMPLETE.md](../docs/PHASE_6_PURGE_COMPLETE.md#optional-audit-log-migration) |
| `Verify-NativeAOT.ps1` | [NATIVE_AOT_VERIFICATION.md](../docs/NATIVE_AOT_VERIFICATION.md) |

**Quick Reference:** [PHASE_6_PURGE_QUICK_REF.md](../docs/PHASE_6_PURGE_QUICK_REF.md)
**Architecture Diagrams:** [PHASE_6_ARCHITECTURE_DIAGRAMS.md](../docs/PHASE_6_ARCHITECTURE_DIAGRAMS.md)
**Executive Summary:** [EXECUTIVE_SUMMARY_PHASE_6.md](../docs/EXECUTIVE_SUMMARY_PHASE_6.md)

---

## 🎓 Training Resources

### For New Operations Team Members

**Required Reading (30 minutes):**
1. [PHASE_6_PURGE_QUICK_REF.md](../docs/PHASE_6_PURGE_QUICK_REF.md) - Quick reference card
2. [PHASE_6_ARCHITECTURE_DIAGRAMS.md](../docs/PHASE_6_ARCHITECTURE_DIAGRAMS.md) - Visual guide

**Deep Dive (2 hours):**
3. [EXECUTIVE_SUMMARY_PHASE_6.md](../docs/EXECUTIVE_SUMMARY_PHASE_6.md) - Business context
4. [PHASE_6_PURGE_COMPLETE.md](../docs/PHASE_6_PURGE_COMPLETE.md) - Technical details

### Key Concepts

**"Ghost Code"** - Legacy type resolution infrastructure that adds overhead
**"No-Ghost Protocol"** - Database verification before purge deployment
**"Pure Canonical"** - Architecture with zero migration residuals
**"Safe Harbor"** - Phase 5 state with backward compatibility

---

## ✅ Sign-Off Criteria

Before using these scripts in production:

- [ ] Reviewed documentation links above
- [ ] Tested scripts in staging environment
- [ ] Backup procedures verified
- [ ] Rollback plan rehearsed
- [ ] On-call engineer notified
- [ ] Incident response playbook updated

---

**Last Updated:** February 4, 2026
**Maintained By:** Platform Engineering Team
**Certification:** Microsoft MVP Hall of Fame - Principal .NET Performance Architect

**Questions?** See [TROUBLESHOOTING.md](../docs/TROUBLESHOOTING.md) or contact Platform Team Lead.
