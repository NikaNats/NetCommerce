// Locate every Roboto/Arial mention in the fetched artifacts, with context.
import { readFileSync } from 'node:fs';

// Same .probe/ directory check-render.mjs writes to. See the note there: /tmp is
// not shared across the sandbox boundary, so these files must live in the project.
const PROBE_DIR = process.env.PROBE_DIR ?? '.probe';

for (const file of [`${PROBE_DIR}/rendered.html`, `${PROBE_DIR}/rendered.css`]) {
  const text = readFileSync(file, 'utf8');
  console.log(`\n=== ${file} (${text.length} bytes) ===`);

  for (const term of ['Roboto', 'Arial', 'Inter']) {
    let idx = text.indexOf(term);
    let n = 0;
    while (idx !== -1 && n < 5) {
      const start = Math.max(0, idx - 90);
      const end = Math.min(text.length, idx + term.length + 60);
      console.log(`  [${term}] …${text.slice(start, end).replace(/\s+/g, ' ')}…`);
      idx = text.indexOf(term, idx + 1);
      n += 1;
    }
    if (n === 0) console.log(`  [${term}] not present`);
  }
}