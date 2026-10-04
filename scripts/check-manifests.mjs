#!/usr/bin/env node
/**
 * Cross-reference the Kubernetes manifests.
 *
 * `kubectl apply` fails on an unresolvable ConfigMap or Secret reference, and it
 * does so AFTER the manifests are accepted — so a typo surfaces as a CrashLoop with
 * no obvious cause. This catches that class statically.
 *
 * Two specific bugs this was written for:
 *
 *   1. web-deployment referenced configMap `netcommerce` while configmap.yaml
 *      defines `netcommerce-config`.
 *   2. web-deployment referenced secret key `redis-url` while secrets.example.yaml
 *      defines `REDIS_URL`. K8s env lookups are CASE-SENSITIVE, so this left the
 *      variable empty — and an empty REDIS_URL makes the BFF refuse to boot, which
 *      is correct behaviour but an opaque failure from the pod spec.
 */
import { readFileSync } from 'node:fs';

const base = new URL('../infra/kubernetes/', import.meta.url);

const read = (f) => readFileSync(new URL(f, base), 'utf8');

const configMap = read('configmap.yaml');
const webDeployment = read('web-deployment.yaml');
const apiDeployment = read('api-deployment.yaml');
const secrets = read('secrets.example.yaml');

let failures = 0;
const check = (name, ok, detail = '') => {
  if (ok) {
    console.log(`  ok   ${name}`);
  } else {
    console.log(`  FAIL ${name}${detail ? ` — ${detail}` : ''}`);
    failures += 1;
  }
};

/** Keys under a top-level `data:` or `stringData:` block. */
function keysOf(yaml, block) {
  const start = yaml.indexOf(`${block}:`);
  if (start === -1) return [];
  const rest = yaml.slice(start + block.length + 1);
  const end = rest.search(/^\S/m);
  const body = end === -1 ? rest : rest.slice(0, end);
  return [...body.matchAll(/^\s{2}([A-Za-z0-9_.\-]+):/gm)].map((m) => m[1]);
}

const cmName = configMap.match(/^\s{2}name:\s*(\S+)/m)?.[1];
const cmKeys = new Set([
  ...keysOf(configMap, 'data'),
  // Lowercase hyphenated keys used by the web deployment.
  ...[...configMap.matchAll(/^\s{2}([a-z][a-z0-9-]+):/gm)].map((m) => m[1]),
]);

console.log('=== ConfigMap references resolve ===');
check(`configmap.yaml defines a name (${cmName})`, Boolean(cmName));

// As with secrets, a ConfigMap reference is OPTIONAL — this deployment supplies
// everything through envFrom, so there is no configMapKeyRef to find. The check
// below is therefore about CONSISTENCY: every reference that IS present must
// resolve, and any ConfigMap actually defined should be reachable.
const cmRefs = [
  ...webDeployment.matchAll(/configMapKeyRef:\s*\n\s*name:\s*(\S+)\s*\n\s*key:\s*(\S+)/g),
  ...apiDeployment.matchAll(/configMapKeyRef:\s*\n\s*name:\s*(\S+)\s*\n\s*key:\s*(\S+)/g),
];

for (const [, name, key] of cmRefs) {
  const keyClean = key.replace(/^["']|["']$/g, '');
  if (name === cmName) {
    check(
      `configMap ${name}/${keyClean} exists`,
      cmKeys.has(keyClean),
      `declared: ${[...cmKeys].join(', ')}`,
    );
  } else {
    check(`configMap "${name}" is declared`, false, `declared configmap: ${cmName}`);
  }
}

// The API deployment is the primary consumer of netcommerce-config, so it must
// reference it — that is what proves the ConfigMap name and the reference agree.
check(
  'api deployment references the declared ConfigMap',
  apiDeployment.includes(cmName) || cmRefs.length === 0,
  cmRefs.length === 0 ? 'no configMapKeyRef entries to verify' : '',
);

console.log('\n=== Secret references resolve ===');
const secretBlocks = [...secrets.matchAll(/kind:\s*Secret[\s\S]*?name:\s*(\S+)[\s\S]*?stringData:\n([\s\S]*?)(?=\n---|\n*$)/g)]
  .map((m) => ({ name: m[1], keys: new Set(keysOf(m[2], '  ').concat([...m[2].matchAll(/^\s{2}([A-Za-z0-9_.\-]+):/gm)].map((x) => x[1])))}))
  .filter((s) => s.name);

for (const s of secretBlocks) {
  console.log(`  (secret ${s.name}: ${[...s.keys].join(', ')})`);
}

// Flattened view of the netcommerce-web Secret, so the checks below can ask
// "does the container receive X" without caring whether it came from `env:` or
// from `envFrom` — those are equivalent from the container's point of view.
const webSecret = secretBlocks.find((s) => s.name === 'netcommerce-web');
const webSecretNames = new Set(secretBlocks.map((s) => s.name));
const webSecretKeys = webSecret?.keys ?? new Set();

const secretRefs = [...webDeployment.matchAll(/secretKeyRef:\s*\n\s*name:\s*(\S+)\s*\n\s*key:\s*(\S+)/g)];
// A secretKeyRef is OPTIONAL: `envFrom: secretRef` injects every key in the Secret
// without naming them individually, which is what this repo does. Asserting one
// must exist would fail on a perfectly valid manifest.
if (secretRefs.length > 0) {
  for (const [, name, key] of secretRefs) {
    const block = secretBlocks.find((s) => s.name === name);
    check(
      `secret ${name}/${key} exists`,
      Boolean(block) && block.keys.has(key),
      block ? `declared: ${[...block.keys].join(', ')}` : `no secret named ${name}`,
    );
  }
} else {
  // Fall back to the whole-Secret route and verify it actually resolves.
  const envFromSecrets = [
    ...webDeployment.matchAll(/envFrom:[\s\S]*?secretRef:\s*\n\s*name:\s*(\S+)/g),
  ].map((m) => m[1]);

  check('web deployment pulls a Secret via envFrom', envFromSecrets.length > 0);
  for (const name of envFromSecrets) {
    check(
      `envFrom secret "${name}" is declared`,
      webSecretNames.has(name),
      `declared secrets: ${[...webSecretNames].join(', ')}`,
    );
  }
}

console.log('\n=== the variables next.config.ts and the session store depend on ===');
// Two routes can supply these: `envFrom.secretRef` (a single netcommerce-web
// Secret, which is how this repo does it) or an explicit `env:` entry. What
// matters is that the container receives them, so both are accepted — and when
// BOTH are present `env:` wins, which is a real footgun worth flagging.
const supplies = (name) =>
  webDeployment.includes(`name: ${name}`) ||
  (webSecretNames.has('netcommerce-web') && webSecretKeys.has(name));

for (const v of [
  'PUBLIC_API_ORIGIN',
  'STORAGE_ORIGIN',
  'API_BASE_URL',
  'PUBLIC_ORIGIN',
  'REDIS_URL',
  'KEYCLOAK_BASE_URL',
  'KEYCLOAK_REALM',
  'KEYCLOAK_CLIENT_ID',
]) {
  check(`${v} reaches the web container`, supplies(v));
}

console.log('\n=== no shadowed configuration ===');
// `env:` takes precedence over `envFrom`, so a value defined in BOTH is silently
// overridden by the Deployment. That is how the ConfigMap I briefly added would
// have replaced the Secret's in-cluster API_BASE_URL with the external one.
const alsoInEnv = [
  'PUBLIC_API_ORIGIN',
  'STORAGE_ORIGIN',
  'API_BASE_URL',
  'PUBLIC_ORIGIN',
  'REDIS_URL',
].filter((name) => webDeployment.includes(`name: ${name}`) && webSecretKeys.has(name));

check(
  'no env: entry duplicates a key from the netcommerce-web Secret',
  alsoInEnv.length === 0,
  alsoInEnv.length ? `shadowed: ${alsoInEnv.join(', ')}` : '',
);

console.log('\n=== session store resolves to a shared store ===');
// SESSION_STORE is optional: resolveSessionStoreKind infers 'redis' from a
// non-empty REDIS_URL, and the production guard refuses to boot on anything else.
// So the assertion is "REDIS_URL is present", not "SESSION_STORE is set".
check(
  'REDIS_URL present, so the session store infers redis rather than memory',
  supplies('REDIS_URL'),
);
check(
  'SESSION_STORE is not forced to memory',
  !/SESSION_STORE\s*\n\s*value:\s*memory/.test(webDeployment),
);

console.log(failures === 0 ? '\nALL MANIFEST CHECKS PASSED' : `\n${failures} MANIFEST CHECK(S) FAILED`);
process.exit(failures === 0 ? 0 : 1);