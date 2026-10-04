// Validate every workflow the way GitHub does: parse YAML, then reject
// GitHub-Actions-specific syntax errors that plain YAML accepts.
//
// The release.yml failure was NOT a YAML error — `:?` inside ${{ }} parses as a
// valid YAML string and fails only when Actions evaluates the expression. So a
// plain YAML parse would have passed. This checks both layers.
//
// Usage: node validate-workflows.mjs
import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);

// js-yaml lives in the frontend workspace (it is a transitive dep there), not at
// the repo root. Resolving from the repo root fails, so try the known location.
let load;
for (const spec of [
  'js-yaml',
  join(process.cwd(), 'src/Web/netcommerce-web/node_modules/js-yaml'),
]) {
  try {
    load = require(spec);
    break;
  } catch {
    /* try next */
  }
}

if (!load) {
  console.error('SKIP: js-yaml not resolvable');
  process.exit(2);
}

const parse = load.load;

const wfDir = '.github/workflows';
const files = readdirSync(wfDir).filter((f) => f.endsWith('.yml') || f.endsWith('.yaml'));

let failures = 0;

for (const file of files) {
  const raw = readFileSync(join(wfDir, file), 'utf8');
  const problems = [];

  // 1. YAML must parse.
  let doc;
  try {
    doc = parse(raw);
  } catch (e) {
    problems.push(`YAML parse error: ${e.message}`);
  }

  // 2. Expression-level checks — the ones a YAML parser cannot see.
  //
  //    Scanned against the PARSED document, not the raw text. A comment in a
  //    `run:` block may legitimately quote the broken syntax to explain why it is
  //    wrong; YAML comments are not in the parsed tree, so they are not flagged.
  //
  //    `${{ ... }}` must not contain shell parameter expansion. Actions has no
  //    ternary or default-value operator, so `${{ x:?msg }}` is a hard parse
  //    error — GitHub rejects the whole workflow before any step runs.
  const walk = (node, path = []) => {
    if (typeof node === 'string') {
      for (const m of node.matchAll(/\$\{\{([\s\S]*?)\}\}/g)) {
        if (/:\?/.test(m[1])) {
          problems.push(
            `shell ":?" expansion inside ${{ }} at ${path.join('.')}: {{${m[1]}}}`,
          );
        }
      }
      return;
    }
    if (Array.isArray(node)) {
      node.forEach((v, i) => walk(v, [...path, i]));
      return;
    }
    if (node && typeof node === 'object') {
      for (const [k, v] of Object.entries(node)) walk(v, [...path, k]);
    }
  };
  if (doc) walk(doc);

  // 3. Structural sanity: every workflow needs on/jobs.
  if (doc && typeof doc === 'object') {
    if (!('on' in doc) && !('jobs' in doc)) {
      problems.push('missing both "on" and "jobs" keys');
    }
    if (doc.jobs && typeof doc.jobs === 'object' && Object.keys(doc.jobs).length === 0) {
      problems.push('"jobs" is empty');
    }
  }

  if (problems.length) {
    failures++;
    console.log(`FAIL ${file}`);
    for (const p of problems) console.log(`       ${p}`);
  } else {
    console.log(`ok   ${file}`);
  }
}

console.log(`\n${files.length} workflow(s) checked, ${failures} with problems.`);
process.exit(failures === 0 ? 0 : 1);