// Structural validation for the GitHub Actions workflows.
//
// A malformed workflow fails at the moment it matters most — on the release tag —
// and `actionlint` is not available here, so the parse itself is asserted instead.
// js-yaml already lives in the frontend's node_modules; this borrows it rather than
// adding a dependency.
import { createRequire } from 'node:module';
import { readFileSync } from 'node:fs';

const require = createRequire(
  new URL('../src/Web/netcommerce-web/package.json', import.meta.url),
);
const yaml = require('js-yaml');

const files = ['pr-validation.yml', 'frontend.yml', 'release.yml', 'native-aot-verification.yml'];

let failures = 0;
for (const f of files) {
  const path = new URL(`../.github/workflows/${f}`, import.meta.url);
  let doc;
  try {
    doc = yaml.load(readFileSync(path, 'utf8'));
  } catch (e) {
    console.log(`  FAIL ${f} — ${e.message.split('\n')[0]}`);
    failures += 1;
    continue;
  }

  const jobs = Object.keys(doc?.jobs ?? {});
  console.log(`  ok   ${f} — jobs: ${jobs.join(', ')}`);

  if (!jobs.length) {
    console.log(`       FAIL ${f} defines no jobs`);
    failures += 1;
    continue;
  }

  for (const [name, job] of Object.entries(doc.jobs)) {
    // A job that declares services must also declare the env those services need,
    // or the host fails to start and the failures look like product bugs.
    const svc = Object.keys(job.services ?? {});
    const connStrings = Object.keys(job.env ?? {}).filter((k) =>
      k.startsWith('CONNECTIONSTRINGS'),
    ).length;

    if (svc.includes('postgres') && connStrings === 0) {
      console.log(
        `       FAIL ${f}:${name} declares a postgres service but no CONNECTIONSTRINGS__* env`,
      );
      failures += 1;
    }
    if (svc.includes('postgres')) {
      console.log(
        `       ok   ${f}:${name} postgres + ${connStrings} connection strings`,
      );
    }
  }
}

console.log(failures === 0 ? '\nALL WORKFLOW CHECKS PASSED' : `\n${failures} WORKFLOW CHECK(S) FAILED`);
process.exit(failures === 0 ? 0 : 1);