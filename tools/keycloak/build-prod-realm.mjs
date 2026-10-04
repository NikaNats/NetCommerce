/**
 * Derives a production-safe Keycloak realm from the development realm JSON.
 *
 * The dev realm (src/NetCommerce.AppHost/realms/netcommerce-realm.json) ships
 * known user passwords, known client secrets, an ROPC test client and open
 * registration. Importing it outside local dev creates known-credential
 * accounts. This produces a prod candidate with all of that removed:
 *
 *   - users[] dropped entirely (provisioned by the identity team instead)
 *   - client 'netcommerce-test' (direct-access grants) dropped
 *   - every remaining client secret replaced with MUST_BE_REPLACED
 *   - registrationAllowed=false
 *
 * It then FAILS if any known-secret material or seeded credential survives,
 * so a future dev-realm addition cannot silently leak into prod output.
 *
 * Usage: node tools/keycloak/build-prod-realm.mjs
 * Writes: tools/keycloak/netcommerce-realm.prod.json
 */
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const devPath = join(here, '..', '..', 'src', 'NetCommerce.AppHost', 'realms', 'netcommerce-realm.json');
const outPath = join(here, 'netcommerce-realm.prod.json');

const realm = JSON.parse(readFileSync(devPath, 'utf8'));

// 1. No seeded users in prod. Ever.
delete realm.users;

// 2. No ROPC test client (password grants for a known secret).
realm.clients = (realm.clients ?? []).filter((c) => c.clientId !== 'netcommerce-test');

// 3. No usable secrets: every remaining client secret becomes an explicit
//    marker the deployment MUST replace.
for (const client of realm.clients) {
  if (client.secret != null) {
    client.secret = 'MUST_BE_REPLACED-via-secret-store';
  }
}

// 4. No open registration.
realm.registrationAllowed = false;

const out = JSON.stringify(realm, null, 2) + '\n';
writeFileSync(outPath, out);

// 5. Verification gate: known-secret material must be gone from the OUTPUT.
const violations = [];
for (const pattern of [
  'Admin123!',
  'Customer123!',
  'Vendor123!',
  'netcommerce-api-secret',
  'netcommerce-service-secret',
  'test-secret-only-for-dev',
  '"users"',
]) {
  if (out.includes(pattern)) violations.push(`surviving material: ${pattern}`);
}
if (/"directAccessGrantsEnabled"\s*:\s*true/.test(out)) {
  violations.push('a client still has directAccessGrantsEnabled=true');
}
if (/"registrationAllowed"\s*:\s*true/.test(out)) {
  violations.push('registrationAllowed is still true');
}

if (violations.length > 0) {
  console.error(`Prod realm verification FAILED:\n - ${violations.join('\n - ')}`);
  process.exit(1);
}

console.log(
  `Prod realm written to ${outPath} (users: 0, ROPC client removed, secrets marked, registration closed).`,
);
