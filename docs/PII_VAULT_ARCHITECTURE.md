# PII Vault Pattern - Architecture Diagrams

## 1. System Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         NetCommerce Application                          │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐              │
│  │   Order      │    │   Shipping   │    │   Payment    │              │
│  │   Module     │    │   Module     │    │   Module     │              │
│  ├──────────────┤    ├──────────────┤    ├──────────────┤              │
│  │ OrderId      │    │ ShipmentId   │    │ PaymentId    │              │
│  │ ProfileId ───┼───▶│ ProfileId ───┼───▶│ ProfileId    │              │
│  │ Amount       │    │ TrackingNo   │    │ Amount       │              │
│  │ Status       │    │ Carrier      │    │ Status       │              │
│  └──────────────┘    └──────────────┘    └──────────────┘              │
│         │                    │                    │                      │
│         └────────────────────┼────────────────────┘                      │
│                              ↓ ProfileId Token                           │
│  ┌─────────────────────────────────────────────────────────┐            │
│  │              PII Vault Store (Restricted Access)        │            │
│  ├─────────────────────────────────────────────────────────┤            │
│  │ ProfileId (PK)            │ Blind Indexes               │            │
│  │ EncryptedFullName ▒▒▒▒▒   │ EmailBlindIndex (HMAC)     │            │
│  │ EncryptedEmail ▒▒▒▒▒▒▒▒   │ PhoneBlindIndex (HMAC)     │            │
│  │ EncryptedPhone ▒▒▒▒▒▒▒▒   │                             │            │
│  │ EncryptedAddress ▒▒▒▒▒▒   │ Key Rotation                │            │
│  │ EncryptedDOB ▒▒▒▒▒▒▒▒▒▒   │ CurrentKeyVersion: v1       │            │
│  │ EncryptedNationalId ▒▒▒   │ LastKeyRotationAt           │            │
│  │                            │                             │            │
│  │ GDPR Compliance            │ Access Tracking             │            │
│  │ IsDeleted: false           │ LastAccessedAt              │            │
│  │ DeletedAt: null            │ LastAccessedByUserId        │            │
│  └─────────────────────────────────────────────────────────┘            │
│                              ↕ AES-256-GCM                               │
│  ┌─────────────────────────────────────────────────────────┐            │
│  │           IEncryptionService (Transparent Layer)         │            │
│  │  • EncryptAsync(plaintext, isDeterministic)             │            │
│  │  • DecryptAsync(encryptedData)                           │            │
│  │  • ComputeBlindIndexAsync(plaintext) → HMAC-SHA256       │            │
│  └─────────────────────────────────────────────────────────┘            │
│                              ↕ Envelope Encryption                       │
│  ┌─────────────────────────────────────────────────────────┐            │
│  │         IKeyManagementService (Azure Key Vault)          │            │
│  │  • GenerateDataKeyAsync() → (DEK, EncryptedDEK)         │            │
│  │  • DecryptDataKeyAsync(encryptedDEK) → DEK              │            │
│  │  • GetCurrentKeyVersionAsync() → "v2"                   │            │
│  └─────────────────────────────────────────────────────────┘            │
│                              ↓                                           │
└──────────────────────────────┼───────────────────────────────────────────┘
                               ↓
                 ┌─────────────────────────┐
                 │  Azure Key Vault / KMS  │
                 │  Master Key (KEK)       │
                 │  • Wrap/Unwrap DEK      │
                 │  • Auto-rotation: 365d  │
                 └─────────────────────────┘
```

---

## 2. Blind Index Search Flow

**Problem:** How do you search for "user@example.com" when all emails are encrypted?

**Solution:** Blind Index (HMAC-SHA256 deterministic hash)

```
┌─────────────────────────────────────────────────────────────────────────┐
│ Step 1: User Registration                                                │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  Input: email = "alice@example.com"                                      │
│                                                                           │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ 1a. Encrypt Email (Deterministic Mode)                            │  │
│  │     IV = SHA256("alice@example.com").Take(16)  ← Same every time  │  │
│  │     Ciphertext = AES256_GCM.Encrypt("alice@example.com", DEK, IV) │  │
│  │     → "XyZ123...ABC789"                                            │  │
│  └───────────────────────────────────────────────────────────────────┘  │
│                                                                           │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ 1b. Compute Blind Index (HMAC-SHA256)                             │  │
│  │     Salt = "secret-blind-index-salt-v1" (from Key Vault)          │  │
│  │     BlindIndex = HMAC_SHA256("alice@example.com", Salt)           │  │
│  │     → "7a3f2c9e1b4d..."                                            │  │
│  └───────────────────────────────────────────────────────────────────┘  │
│                                                                           │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ 1c. Store in Database                                              │  │
│  │     INSERT INTO pii_vault_entries (                                │  │
│  │       profile_id,                                                  │  │
│  │       encrypted_email,        -- "XyZ123...ABC789"                │  │
│  │       email_blind_index       -- "7a3f2c9e1b4d..."                │  │
│  │     )                                                              │  │
│  └───────────────────────────────────────────────────────────────────┘  │
│                                                                           │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│ Step 2: Searching for Email (3 months later)                            │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  Query: "Find orders for alice@example.com"                              │
│                                                                           │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ 2a. Compute Blind Index for Search Term                           │  │
│  │     SearchTerm = "alice@example.com"                               │  │
│  │     Salt = "secret-blind-index-salt-v1" (same salt!)              │  │
│  │     SearchIndex = HMAC_SHA256("alice@example.com", Salt)          │  │
│  │     → "7a3f2c9e1b4d..."  ← SAME HASH AS REGISTRATION!             │  │
│  └───────────────────────────────────────────────────────────────────┘  │
│                                                                           │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ 2b. Index Seek (O(1) Lookup)                                      │  │
│  │     SELECT * FROM pii_vault_entries                                │  │
│  │     WHERE email_blind_index = '7a3f2c9e1b4d...'                   │  │
│  │     ↓                                                              │  │
│  │     Index Scan using idx_email_blind_index (1.2ms) ← FAST!        │  │
│  │     Returns: profile_id = "550e8400-e29b-..."                     │  │
│  └───────────────────────────────────────────────────────────────────┘  │
│                                                                           │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ 2c. Decrypt ONLY the Matched Row                                  │  │
│  │     EncryptedEmail = "XyZ123...ABC789"                             │  │
│  │     PlaintextEmail = AES256_GCM.Decrypt(EncryptedEmail, DEK)      │  │
│  │     → "alice@example.com"  ← Confirmed match!                     │  │
│  └───────────────────────────────────────────────────────────────────┘  │
│                                                                           │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ 2d. Find Orders by ProfileId Token                                │  │
│  │     SELECT * FROM orders                                           │  │
│  │     WHERE profile_id = '550e8400-e29b-...'                        │  │
│  │     ↓                                                              │  │
│  │     Returns: 5 orders for this customer                           │  │
│  └───────────────────────────────────────────────────────────────────┘  │
│                                                                           │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│ Security Properties                                                      │
├─────────────────────────────────────────────────────────────────────────┤
│ ✅ Deterministic Hash: Same email → Same index (enables searches)       │
│ ✅ One-way Hash: Can't reverse "7a3f2c9e..." → "alice@example.com"      │
│ ✅ Secret Salt: Attacker without salt can't precompute rainbow tables   │
│ ✅ O(1) Lookup: PostgreSQL index seek (1-2ms) vs full table scan (45s)  │
│ ✅ Minimal Decryption: Only decrypt matched row, not entire table        │
│ ❌ Frequency Analysis: If 1000 users have same email, same blind index  │
│    → Mitigation: Add user-specific salt for deterministic encryption    │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Envelope Encryption Flow

**Problem:** Key rotation requires re-encrypting millions of rows (hours of downtime).

**Solution:** Envelope Encryption (Master KEK + Data DEK per customer)

```
┌─────────────────────────────────────────────────────────────────────────┐
│ Registration: Encrypt Customer PII                                       │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ Step 1: Generate Data Encryption Key (DEK)                      │    │
│  │                                                                  │    │
│  │  Request to Azure Key Vault:                                    │    │
│  │    POST /keys/pii-master-key-v1/generateDataKey                 │    │
│  │                                                                  │    │
│  │  Response:                                                       │    │
│  │    PlaintextDEK:   [32 random bytes] ← Used to encrypt PII      │    │
│  │    EncryptedDEK:   [48 bytes]        ← Encrypted by Master KEK  │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                           │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ Step 2: Encrypt PII with DEK (AES-256-GCM)                      │    │
│  │                                                                  │    │
│  │  Input:  "alice@example.com"                                    │    │
│  │  Key:    PlaintextDEK [32 bytes]                                │    │
│  │  IV:     [16 random bytes]                                      │    │
│  │                                                                  │    │
│  │  Ciphertext = AES256_GCM.Encrypt(                               │    │
│  │    data: "alice@example.com",                                   │    │
│  │    key:  PlaintextDEK,                                          │    │
│  │    iv:   [random IV]                                            │    │
│  │  )                                                               │    │
│  │  → "XyZ123...ABC789"                                            │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                           │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ Step 3: Store Encrypted Data + Encrypted DEK                    │    │
│  │                                                                  │    │
│  │  Database Record:                                                │    │
│  │  ┌────────────────────────────────────────────────────────────┐ │    │
│  │  │ encrypted_email:                                            │ │    │
│  │  │   "key-v1|IV_base64|Ciphertext_base64|EncryptedDEK_base64" │ │    │
│  │  │    ↑        ↑              ↑                   ↑            │ │    │
│  │  │  Master   Random     Encrypted Email   DEK Encrypted by KEK │ │    │
│  │  │  Key ID     IV                                              │ │    │
│  │  └────────────────────────────────────────────────────────────┘ │    │
│  │                                                                  │    │
│  │  ⚠️ CRITICAL: Plaintext DEK is NEVER stored in database!        │    │
│  │               Only the encrypted version (by Master KEK)        │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                           │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│ Decryption: Read Customer PII                                            │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ Step 1: Parse Encrypted Data from Database                      │    │
│  │                                                                  │    │
│  │  Stored Value:                                                   │    │
│  │    "key-v1|IV_base64|Ciphertext_base64|EncryptedDEK_base64"    │    │
│  │                                                                  │    │
│  │  Parse:                                                          │    │
│  │    KeyId = "key-v1"                                             │    │
│  │    IV = [16 bytes]                                              │    │
│  │    Ciphertext = "XyZ123...ABC789"                               │    │
│  │    EncryptedDEK = [48 bytes]                                    │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                           │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ Step 2: Decrypt Data Encryption Key (DEK) via Azure Key Vault  │    │
│  │                                                                  │    │
│  │  Request to Azure Key Vault:                                    │    │
│  │    POST /keys/pii-master-key-v1/unwrapKey                       │    │
│  │    Body: { "value": "EncryptedDEK_base64" }                     │    │
│  │                                                                  │    │
│  │  Response:                                                       │    │
│  │    PlaintextDEK: [32 bytes] ← Decrypted by Master KEK           │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                           │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ Step 3: Decrypt PII with DEK (AES-256-GCM)                      │    │
│  │                                                                  │    │
│  │  Plaintext = AES256_GCM.Decrypt(                                │    │
│  │    ciphertext: "XyZ123...ABC789",                               │    │
│  │    key:        PlaintextDEK,                                    │    │
│  │    iv:         [16 bytes from storage]                          │    │
│  │  )                                                               │    │
│  │  → "alice@example.com"                                          │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                           │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│ Key Rotation: Zero-Downtime Master Key Rotation                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  Scenario: Master Key "key-v1" compromised → Rotate to "key-v2"         │
│                                                                           │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ Step 1: Decrypt with OLD Key ("key-v1")                         │    │
│  │                                                                  │    │
│  │  Parse Database:                                                 │    │
│  │    KeyId = "key-v1"                                             │    │
│  │    EncryptedDEK_OLD = [48 bytes]                                │    │
│  │                                                                  │    │
│  │  Request to Azure Key Vault:                                    │    │
│  │    POST /keys/pii-master-key-v1/unwrapKey  ← OLD KEY            │    │
│  │    → PlaintextDEK = [32 bytes]                                  │    │
│  │                                                                  │    │
│  │  Decrypt PII:                                                    │    │
│  │    Plaintext = AES256_GCM.Decrypt(Ciphertext, PlaintextDEK)     │    │
│  │    → "alice@example.com"                                        │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                           │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ Step 2: Re-encrypt with NEW Key ("key-v2")                      │    │
│  │                                                                  │    │
│  │  Generate NEW Data Key:                                          │    │
│  │    POST /keys/pii-master-key-v2/generateDataKey  ← NEW KEY      │    │
│  │    → PlaintextDEK_NEW, EncryptedDEK_NEW                         │    │
│  │                                                                  │    │
│  │  Encrypt PII with NEW DEK:                                       │    │
│  │    NewCiphertext = AES256_GCM.Encrypt(                          │    │
│  │      data: "alice@example.com",                                 │    │
│  │      key:  PlaintextDEK_NEW,                                    │    │
│  │      iv:   [new random IV]                                      │    │
│  │    )                                                             │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                           │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ Step 3: Update Database with NEW Encrypted Data                 │    │
│  │                                                                  │    │
│  │  New Storage Format:                                             │    │
│  │    "key-v2|NewIV|NewCiphertext|EncryptedDEK_NEW"               │    │
│  │     ↑ Updated to v2                                             │    │
│  │                                                                  │    │
│  │  UPDATE pii_vault_entries                                        │    │
│  │  SET encrypted_email = 'key-v2|...',                            │    │
│  │      current_key_version = 'key-v2',                            │    │
│  │      last_key_rotation_at = NOW()                               │    │
│  │  WHERE profile_id = '550e8400-...'                              │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                           │
│  ✅ Result: Record now uses "key-v2" (new master key)                   │
│  ✅ Old records with "key-v1" still readable (backwards compatible)     │
│  ✅ Gradual rollout: Rotate 1000 records/hour (no downtime)             │
│                                                                           │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│ Benefits of Envelope Encryption                                          │
├─────────────────────────────────────────────────────────────────────────┤
│ ✅ Fast Key Rotation: Only re-encrypt DEK (48 bytes), not PII (KB)      │
│ ✅ Zero Downtime: Old and new keys work simultaneously during rotation   │
│ ✅ Per-Customer Keys: Each customer has unique DEK (blast radius = 1)    │
│ ✅ Compliance: Master KEK never leaves Key Vault (FIPS 140-2 Level 3)   │
│ ✅ Gradual Rollout: Rotate in batches (1000/hour) without locking table │
│ ❌ Extra Round-Trip: Decrypt DEK via KMS before decrypting PII (+5ms)   │
│    → Mitigation: Cache decrypted DEKs in memory (with TTL)              │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 4. GDPR "Right to be Forgotten" Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│ Day 0: Customer Registration                                             │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ┌────────────────┐         ┌────────────────┐        ┌──────────────┐  │
│  │  Order Table   │         │  Shipping Tbl  │        │  PII Vault   │  │
│  ├────────────────┤         ├────────────────┤        ├──────────────┤  │
│  │ OrderId: 1001  │         │ ShipmentId: 5  │        │ ProfileId:   │  │
│  │ ProfileId: ────┼────┐    │ ProfileId: ────┼───┐    │ 550e8400-... │  │
│  │   550e8400-... │    │    │   550e8400-... │   │    │              │  │
│  │ Amount: $99    │    │    │ Carrier: USPS  │   │    │ Email: ▒▒▒▒  │  │
│  │ Status: Paid   │    │    │ Status: Shipped│   │    │ Phone: ▒▒▒▒  │  │
│  └────────────────┘    │    └────────────────┘   │    │ Addr: ▒▒▒▒▒  │  │
│                        │                          │    └──────────────┘  │
│  ┌────────────────┐    │    ┌────────────────┐   │           ↑          │
│  │ Order Table 2  │    │    │ Payment Table  │   │           │          │
│  ├────────────────┤    │    ├────────────────┤   │           │          │
│  │ OrderId: 1002  │    └───▶│ PaymentId: 7   │   └───────────┘          │
│  │ ProfileId: ────┼─────────│ ProfileId: ────┼───────────────           │
│  │   550e8400-... │         │   550e8400-... │                           │
│  │ Amount: $150   │         │ Method: Card   │                           │
│  │ Status: Paid   │         │ Status: Success│                           │
│  └────────────────┘         └────────────────┘                           │
│                                                                           │
│  💡 All business tables store ProfileId TOKEN, not actual PII            │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│ Day 90: Customer Requests GDPR Erasure                                   │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  Customer → POST /api/v1/privacy/forget-me                               │
│                                                                           │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ ForgetCustomerCommand                                            │    │
│  │   ProfileId: 550e8400-...                                        │    │
│  │   Reason: "GDPR Article 17 request"                             │    │
│  │   RequestedBy: customer@example.com                             │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                      ↓                                                    │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ ForgetCustomerHandler (Wolverine)                                │    │
│  │   1. Find PII vault entry by ProfileId                           │    │
│  │   2. MarkAsDeleted() → IsDeleted = true, DeletedAt = NOW()      │    │
│  │   3. Create audit entry (compliance requirement)                 │    │
│  │   4. Publish CustomerForgottenIntegrationEvent                   │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                      ↓                                                    │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │ PII Vault (Soft Delete)                                           │   │
│  ├──────────────────────────────────────────────────────────────────┤   │
│  │ ProfileId: 550e8400-...                                           │   │
│  │ Email: ▒▒▒▒▒▒▒▒▒ (still encrypted)                               │   │
│  │ Phone: ▒▒▒▒▒▒▒▒▒ (still encrypted)                               │   │
│  │                                                                    │   │
│  │ IsDeleted: TRUE            ← Soft delete flag                     │   │
│  │ DeletedAt: 2025-01-24      ← 90-day retention starts             │   │
│  │ DeletionReason: "GDPR Article 17 request"                        │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│                      ↓                                                    │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ CustomerForgottenIntegrationEvent (Published to All Modules)     │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│            ↓                    ↓                    ↓                    │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐          │
│  │ Order Module    │  │ Shipping Module │  │ Catalog Module  │          │
│  │ → Clear cache   │  │ → Scrub logs    │  │ → Delete recs   │          │
│  │ → Anonymize logs│  │                 │  │                 │          │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘          │
│                                                                           │
│  ⚠️ Business tables (orders, payments) are NOT touched!                  │
│     ProfileId = 550e8400-... still exists, but:                          │
│       - Can't resolve to email/phone (vault entry deleted)               │
│       - Orders remain for accounting/legal compliance                    │
│       - Customer is effectively "anonymized"                             │
│                                                                           │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│ Day 180 (90 days later): Background Purge Job                           │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  Hangfire Daily Job → PurgeForgottenCustomersCommand                     │
│                                                                           │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ PurgeForgottenCustomersHandler                                   │    │
│  │   1. Find entries where:                                         │    │
│  │      IsDeleted = true AND DeletedAt < (NOW() - 90 days)         │    │
│  │   2. For each entry: PurgeData()                                 │    │
│  │   3. Publish PiiPurgedIntegrationEvent                           │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                      ↓                                                    │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │ PII Vault (Hard Delete / Cryptographic Erasure)                  │   │
│  ├──────────────────────────────────────────────────────────────────┤   │
│  │ ProfileId: 00000000-0000-... ← Set to Guid.Empty                 │   │
│  │ Email: "purged-a3f2...@deleted.local" ← Random GUID               │   │
│  │ Phone: "XXXXXXXXXXXXXXX" ← Overwritten                           │   │
│  │ Address: "PURGED" ← Overwritten                                  │   │
│  │ DateOfBirth: NULL ← Cleared                                      │   │
│  │ NationalId: NULL ← Cleared                                       │   │
│  │                                                                    │   │
│  │ IsDeleted: TRUE                                                   │   │
│  │ DeletedAt: 2025-01-24                                            │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│                                                                           │
│  ✅ Result: PII is IRREVERSIBLY destroyed (cryptographic erasure)        │
│  ✅ Orders remain in database with ProfileId = 00000000-... (orphaned)   │
│  ✅ Compliance: 90-day retention met, no possibility of data recovery    │
│                                                                           │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│ GDPR Compliance Matrix                                                   │
├─────────────────────────────────────────────────────────────────────────┤
│ Article 17: Right to Erasure                                             │
│   ✅ Soft delete within 30 days of request                               │
│   ✅ Hard delete (purge) after 90-day retention                          │
│   ✅ Audit log of all erasure requests                                   │
│                                                                           │
│ Article 15: Right of Access                                              │
│   ✅ PiiVaultEntry.LastAccessedAt tracks all reads                       │
│   ✅ GET /api/v1/privacy/my-data exports decrypted PII                   │
│                                                                           │
│ Article 32: Security of Processing                                       │
│   ✅ AES-256-GCM authenticated encryption                                │
│   ✅ Envelope encryption (Master KEK in Azure Key Vault)                 │
│   ✅ Blind indexes prevent plaintext searches                            │
│                                                                           │
│ Retention Limits                                                         │
│   ✅ 90-day soft delete retention (configurable)                         │
│   ✅ Automated purge via background job                                  │
│   ✅ Compliance reporting dashboard                                      │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 5. Module Isolation & Least Privilege

```
┌─────────────────────────────────────────────────────────────────────────┐
│ Database Security: Least Privilege Access Control                        │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ┌────────────────────────────────────────────────────────────────┐     │
│  │ Catalog Module (catalog_user database role)                    │     │
│  │                                                                 │     │
│  │ GRANT:                                                          │     │
│  │   SELECT, INSERT, UPDATE, DELETE ON catalog.products           │     │
│  │   SELECT, INSERT, UPDATE, DELETE ON catalog.categories         │     │
│  │                                                                 │     │
│  │ REVOKE:                                                         │     │
│  │   ALL ON pii_vault_entries              ← Can't access PII!    │     │
│  │   ALL ON orders                         ← Can't see orders     │     │
│  └────────────────────────────────────────────────────────────────┘     │
│                                                                           │
│  ┌────────────────────────────────────────────────────────────────┐     │
│  │ Order Module (order_user database role)                         │     │
│  │                                                                 │     │
│  │ GRANT:                                                          │     │
│  │   SELECT, INSERT, UPDATE, DELETE ON orders.orders              │     │
│  │   SELECT, INSERT, UPDATE, DELETE ON orders.order_items         │     │
│  │   SELECT ON pii_vault_entries (profile_id ONLY) ← Token only   │     │
│  │                                                                 │     │
│  │ REVOKE:                                                         │     │
│  │   SELECT ON pii_vault_entries (encrypted_email, encrypted_phone│     │
│  │   encrypted_address, ...)                ← No PII access!       │     │
│  └────────────────────────────────────────────────────────────────┘     │
│                                                                           │
│  ┌────────────────────────────────────────────────────────────────┐     │
│  │ Privacy Module (privacy_user database role)                     │     │
│  │                                                                 │     │
│  │ GRANT:                                                          │     │
│  │   SELECT, INSERT, UPDATE ON pii_vault_entries  ← Full access   │     │
│  │   SELECT, INSERT ON pii_access_audit           ← Audit only    │     │
│  │                                                                 │     │
│  │ REVOKE:                                                         │     │
│  │   DELETE ON pii_vault_entries          ← Append-only vault     │     │
│  │   UPDATE, DELETE ON pii_access_audit   ← Tamper-proof audit    │     │
│  └────────────────────────────────────────────────────────────────┘     │
│                                                                           │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│ Attack Scenario: SQL Injection in Catalog Module                        │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  Attacker: Finds SQL injection in /api/products?search=...               │
│                                                                           │
│  ❌ Attempt 1: Read PII from Vault                                       │
│  ```sql                                                                  │
│  ' OR 1=1; SELECT * FROM pii_vault_entries; --                           │
│  ```                                                                     │
│  Result: ERROR: permission denied for table pii_vault_entries            │
│          (catalog_user role can't access PII vault)                      │
│                                                                           │
│  ❌ Attempt 2: Join Orders to Get ProfileId                              │
│  ```sql                                                                  │
│  ' OR 1=1; SELECT * FROM orders; --                                      │
│  ```                                                                     │
│  Result: ERROR: permission denied for table orders                       │
│          (catalog_user role can't access orders schema)                  │
│                                                                           │
│  ❌ Attempt 3: Read Configuration (KMS keys, connection strings)         │
│  ```sql                                                                  │
│  ' OR 1=1; SELECT * FROM pg_settings WHERE name LIKE '%password%'; --    │
│  ```                                                                     │
│  Result: Returns ONLY catalog module connection string                   │
│          (no KMS keys, no other module credentials)                      │
│                                                                           │
│  ✅ Damage Contained: Attacker gets products data only                   │
│     - No PII (email, phone, address)                                     │
│     - No order history                                                   │
│     - No encryption keys                                                 │
│     - No cross-module access                                             │
│                                                                           │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│ Blast Radius Comparison                                                  │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ❌ Traditional Monolithic Schema (Single Database User)                 │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ SQL Injection in ANY module = Access to ALL data:               │    │
│  │   - Customer PII (email, phone, address, SSN)                   │    │
│  │   - Order history ($150k fraud potential)                        │    │
│  │   - Payment details (encrypted, but exposed)                     │    │
│  │   - Inventory (competitor intelligence)                          │    │
│  │                                                                  │    │
│  │ Blast Radius: 100% of database                                  │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                           │
│  ✅ Vault Pattern + Least Privilege (Per-Module Database Roles)          │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ SQL Injection in Catalog module = Access to:                    │    │
│  │   - Products data (100k products)                               │    │
│  │   - Categories (50 categories)                                  │    │
│  │                                                                  │    │
│  │ Cannot Access:                                                   │    │
│  │   ✅ Customer PII (permission denied)                            │    │
│  │   ✅ Orders (permission denied)                                  │    │
│  │   ✅ Payments (permission denied)                                │    │
│  │   ✅ Encryption keys (not in database)                           │    │
│  │                                                                  │    │
│  │ Blast Radius: 5% of database (products only)                    │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                           │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 6. Performance Optimization Strategies

### 1. DEK Caching (Reduce Key Vault Round-Trips)

```
┌─────────────────────────────────────────────────────────────────────────┐
│ Problem: Every decrypt requires Key Vault round-trip (+5ms latency)     │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ❌ Without Caching:                                                     │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ Request 1: Decrypt email                                        │    │
│  │   → Key Vault: Decrypt DEK (5ms)                                │    │
│  │   → AES: Decrypt email (0.4ms)                                  │    │
│  │   Total: 5.4ms                                                   │    │
│  │                                                                  │    │
│  │ Request 2: Decrypt phone (same customer)                        │    │
│  │   → Key Vault: Decrypt DEK again (5ms)  ← Duplicate!            │    │
│  │   → AES: Decrypt phone (0.4ms)                                  │    │
│  │   Total: 5.4ms                                                   │    │
│  │                                                                  │    │
│  │ 1000 requests = 5400ms (5.4 seconds)                            │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                           │
│  ✅ With DEK Caching (In-Memory Cache with TTL):                         │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ Request 1: Decrypt email                                        │    │
│  │   → Key Vault: Decrypt DEK (5ms)                                │    │
│  │   → Cache: Store PlaintextDEK (TTL: 5 minutes)                  │    │
│  │   → AES: Decrypt email (0.4ms)                                  │    │
│  │   Total: 5.4ms                                                   │    │
│  │                                                                  │    │
│  │ Request 2: Decrypt phone (same customer)                        │    │
│  │   → Cache: Hit! PlaintextDEK retrieved (0.01ms)  ← FAST!        │    │
│  │   → AES: Decrypt phone (0.4ms)                                  │    │
│  │   Total: 0.41ms                                                  │    │
│  │                                                                  │    │
│  │ 1000 requests = 405ms (13x faster!)                             │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                           │
│  ⚠️ Security Trade-off:                                                  │
│    - DEK cached in memory for 5 minutes (configurable)                   │
│    - If server compromised, attacker gets DEKs for recent customers      │
│    - Mitigation: Short TTL (5 min), encrypt cache at rest, HSM storage   │
│                                                                           │
└─────────────────────────────────────────────────────────────────────────┘
```

### 2. Blind Index Sharding (Prevent Rainbow Tables)

```
┌─────────────────────────────────────────────────────────────────────────┐
│ Problem: Same email → Same blind index (frequency analysis attack)      │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ❌ Naive Blind Index:                                                   │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ Email: "admin@company.com"                                       │    │
│  │ BlindIndex = HMAC_SHA256("admin@company.com", "global-salt")   │    │
│  │            = "7a3f2c9e1b4d..."                                   │    │
│  │                                                                  │    │
│  │ Result: ALL 1000 users with "admin@company.com" have            │    │
│  │         the same blind index → Frequency analysis reveals        │    │
│  │         "7a3f2c9e1b4d..." is a popular email                     │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                           │
│  ✅ Sharded Blind Index (Per-Customer Salt):                             │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │ Customer 1:                                                      │    │
│  │   Email: "admin@company.com"                                     │    │
│  │   Salt: "global-salt" + ProfileId("550e8400-...")               │    │
│  │   BlindIndex = HMAC_SHA256("admin@company.com", salt1)         │    │
│  │              = "a1b2c3d4..."                                     │    │
│  │                                                                  │    │
│  │ Customer 2 (same email):                                         │    │
│  │   Email: "admin@company.com"                                     │    │
│  │   Salt: "global-salt" + ProfileId("660f9511-...")               │    │
│  │   BlindIndex = HMAC_SHA256("admin@company.com", salt2)         │    │
│  │              = "e5f6g7h8..."  ← DIFFERENT HASH!                 │    │
│  │                                                                  │    │
│  │ Result: Same email → Different blind indexes                    │    │
│  │         → Frequency analysis defeated                            │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                           │
│  ⚠️ Search Trade-off:                                                    │
│    - Can no longer search by blind index alone (need ProfileId)          │
│    - Solution: Store global blind index + customer-specific index        │
│      (search by global, verify with customer-specific)                   │
│                                                                           │
└─────────────────────────────────────────────────────────────────────────┘
```

---

**Document Version:** 1.0
**Last Updated:** 2025-01-24
**Status:** Implementation Complete, Production Deployment Pending
