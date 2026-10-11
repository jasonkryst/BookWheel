// Files the feature-audit catalog as GitHub issues and generates ISSUES.md.
// Idempotent: issue-map.json records key → issue number; existing keys are skipped.
//
//   node docs/audits/feature-audit-2026-10/file-issues.mjs            # labels + issues + epic + ISSUES.md
//   node docs/audits/feature-audit-2026-10/file-issues.mjs --dry-run  # print what would be filed
//   node docs/audits/feature-audit-2026-10/file-issues.mjs --only=CORE-10,LIBRARY-7  # re-sync title+body of specific issues

import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync, writeFileSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { areas, issues, tierOf, sortByPriority } from './issue-catalog.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const mapPath = join(here, 'issue-map.json');
const dryRun = process.argv.includes('--dry-run');
const tmp = mkdtempSync(join(tmpdir(), 'bw-audit-'));
const AUDIT_LABEL = 'audit:feature-2026-10';
const DOC_PATH = 'docs/audits/feature-audit-2026-10/README.md';

const IMPACT = { H: 'high', M: 'medium', L: 'low' };
const COMPLEXITY = { S: 'small', M: 'medium', L: 'large' };
const PHASE = { 1: '1-self-hosted', 2: '2-public', 3: '3-saas' };
const PHASE_TEXT = { 1: '1 — Self-hosted', 2: '2 — Public face', 3: '3 — SaaS' };
const TIER_TEXT = { 1: 'Tier 1 — quick win', 2: 'Tier 2', 3: 'Tier 3', 4: 'Tier 4', 5: 'Tier 5 — later' };
const KEY_RE = /\b(AUTH|ACCOUNTS|ADMIN|LIBRARY|WHEEL|LOOKUP|STATS|TRANSFER|PREFS|PWA|OPS|CORE)-\d+\b/g;

const labels = [
  [AUDIT_LABEL, '5319e7', 'Filed by the 2026-10 feature-style audit'],
  ['security', 'b60205', 'Security-relevant change'],
  ...Object.entries(areas).map(([k, v]) => [`area:${k}`, 'c5def5', v]),
  ['impact:high', 'd93f0b', 'High user/operator impact'],
  ['impact:medium', 'fbca04', 'Medium impact'],
  ['impact:low', 'c2e0c6', 'Low impact'],
  ['complexity:small', 'c2e0c6', '≈ ≤1 day'],
  ['complexity:medium', 'fbca04', '≈ 2–5 days'],
  ['complexity:large', 'd93f0b', '≈ 1–3 weeks'],
  ['phase:1-self-hosted', '0e8a16', 'Serves the self-hosted phase'],
  ['phase:2-public', '1d76db', 'Needed for a public-facing launch'],
  ['phase:3-saas', '5319e7', 'Needed for SaaS'],
  ...[1, 2, 3, 4, 5].map(t => [`tier:${t}`, ['b60205', 'd93f0b', 'fbca04', 'c2e0c6', 'ededed'][t - 1], `Priority tier ${t} (impact vs complexity)`]),
];

function gh(args) {
  if (dryRun) { console.log('gh', args.join(' ').slice(0, 160)); return ''; }
  return execFileSync('gh', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
}

// Advisory keys map to { ghsa, url }; they stay as plain keys in public text.
function link(map, text) {
  return text.replace(KEY_RE, k => (typeof map[k] === 'number' ? `#${map[k]}` : k));
}

function ref(map, key) {
  const v = map[key];
  if (v && typeof v === 'object') return '🔒 private advisory';
  return v ? `#${v}` : '?';
}

// Full write-ups for advisory items live in a gitignored file so they never reach the public repo.
const localPath = join(here, 'advisories.local.mjs');
const privateAdvisories = existsSync(localPath)
  ? Object.fromEntries((await import(pathToFileURL(localPath).href)).advisories.map(a => [a.key, a]))
  : {};

function advisoryPayload(a) {
  const description = [
    '## Summary', a.title, '',
    '## Details', a.current, '',
    '## Remediation', a.rec, '',
    '## Acceptance criteria', ...a.ac.map(x => `- ${x}`), '',
    '## References', ...a.refs.map(r => `- ${r}`), '',
    `_Found by the Feature Audit (2026-10), key ${a.key}._`,
  ].join('\n');
  return {
    summary: a.title,
    description,
    severity: a.severity,
    cwe_ids: a.cweIds ?? [],
    vulnerabilities: [{ package: { ecosystem: 'other', name: 'BookWheel' }, vulnerable_version_range: a.vulnerableVersionRange ?? null, patched_versions: null, vulnerable_functions: [] }],
  };
}

function body(issue, map) {
  const tier = tierOf(issue);
  const epic = map.EPIC ? ` · tracking epic #${map.EPIC}` : '';
  return [
    `> Filed by the **Feature Audit (2026-10)** — see [\`${DOC_PATH}\`](https://github.com/jasonkryst/BookWheel/blob/main/${DOC_PATH})${epic}`,
    '',
    `| Area | Impact | Complexity | Phase | Priority |`,
    `|---|---|---|---|---|`,
    `| ${areas[issue.area]} | ${IMPACT[issue.impact]} | ${COMPLEXITY[issue.complexity]} | ${PHASE_TEXT[issue.phase]} | ${TIER_TEXT[tier]} |`,
    '',
    '## Current state',
    link(map, issue.current),
    '',
    '## Recommendation',
    link(map, issue.rec),
    '',
    '## Acceptance criteria',
    ...issue.ac.map(a => `- [ ] ${link(map, a)}`),
    ...(issue.refs.length ? ['', '## References', ...issue.refs.map(r => `- ${link(map, r.replace(/^(BookWheel[^ ]*|docker-compose\.yml|README\.md|IMPROVEMENT_ROADMAP\.md|\.github[^ ]*|docs\/[^ ]*)/, '`$1`'))}`)] : []),
    '',
    `<sub>Audit key: ${issue.key}</sub>`,
  ].join('\n');
}

function labelsFor(issue) {
  return [
    AUDIT_LABEL, issue.type, `area:${issue.area}`, `impact:${IMPACT[issue.impact]}`,
    `complexity:${COMPLEXITY[issue.complexity]}`, `phase:${PHASE[issue.phase]}`, `tier:${tierOf(issue)}`,
    ...(issue.security ? ['security'] : []),
  ];
}

function writeBody(name, text) {
  const p = join(tmp, `${name}.md`);
  writeFileSync(p, text);
  return p;
}

function epicBody(map) {
  const ordered = sortByPriority(issues);
  const lines = [
    `Tracking epic for the **Feature Audit (2026-10)**. Full analysis: [\`${DOC_PATH}\`](https://github.com/jasonkryst/BookWheel/blob/main/${DOC_PATH}).`,
    '',
    'Priority = impact vs complexity (`score = impact×2 − complexity`). Within a tier, items are ordered by phase (self-hosted → public → SaaS), then bugs first.',
    '',
  ];
  for (const t of [1, 2, 3, 4, 5]) {
    const inTier = ordered.filter(i => tierOf(i) === t);
    lines.push(`### ${TIER_TEXT[t]} (${inTier.length})`);
    for (const i of inTier) {
      lines.push(`- [ ] ${ref(map, i.key)} — ${i.title} <sub>(${i.key} · ${IMPACT[i.impact]}/${COMPLEXITY[i.complexity]} · phase ${i.phase})</sub>`);
    }
    lines.push('');
  }
  return lines.join('\n');
}

function issuesMarkdown(map) {
  const ordered = sortByPriority(issues);
  const repoUrl = 'https://github.com/jasonkryst/BookWheel/issues';
  const row = i => `| ${typeof map[i.key] === 'number' ? `[#${map[i.key]}](${repoUrl}/${map[i.key]})` : map[i.key] ? '🔒 private advisory' : '—'} | ${i.key} | ${i.title.replace(/\|/g, '\\|')} | ${i.type}${i.security ? ' 🔒' : ''} | ${IMPACT[i.impact]} | ${COMPLEXITY[i.complexity]} | ${i.phase} |`;
  const head = '| Issue | Key | Title | Type | Impact | Complexity | Phase |\n|---|---|---|---|---|---|---|';
  const out = [
    '# Feature Audit 2026-10 — Issue Index',
    '',
    `Generated by \`file-issues.mjs\` from \`issue-catalog.mjs\`. Tracking epic: ${map.EPIC ? `[#${map.EPIC}](${repoUrl}/${map.EPIC})` : '—'}.`,
    '',
    '## By priority',
    '',
  ];
  for (const t of [1, 2, 3, 4, 5]) {
    out.push(`### ${TIER_TEXT[t]}`, '', head, ...ordered.filter(i => tierOf(i) === t).map(row), '');
  }
  out.push('## By feature', '');
  for (const [k, name] of Object.entries(areas)) {
    out.push(`### ${name}`, '', head, ...ordered.filter(i => i.area === k).map(row), '');
  }
  return out.join('\n');
}

// ── run ──
const map = existsSync(mapPath) ? JSON.parse(readFileSync(mapPath, 'utf8')) : {};
const save = () => { if (!dryRun) writeFileSync(mapPath, JSON.stringify(map, null, 2) + '\n'); };

for (const [name, color, desc] of labels) {
  gh(['label', 'create', name, '--color', color, '--description', desc, '--force']);
}

// Pass 1: create issues in priority order (so lower numbers ≈ higher priority).
for (const issue of sortByPriority(issues)) {
  if (map[issue.key]) continue;
  if (issue.advisory) {
    const a = privateAdvisories[issue.key];
    if (!a) { console.warn(`${issue.key}: advisories.local.mjs missing — skipped`); continue; }
    const input = writeBody(`${issue.key}-advisory`, JSON.stringify(advisoryPayload(a)));
    const res = gh(['api', '-X', 'POST', 'repos/jasonkryst/BookWheel/security-advisories', '--input', input]);
    if (!dryRun) { const j = JSON.parse(res); map[issue.key] = { ghsa: j.ghsa_id, url: j.html_url }; save(); console.log(`${issue.key} → ${j.ghsa_id} (private)`); }
    continue;
  }
  const url = gh(['issue', 'create', '--title', issue.title, '--body-file', writeBody(issue.key, body(issue, map)),
    ...labelsFor(issue).flatMap(l => ['--label', l])]);
  const num = Number(url.split('/').pop());
  if (!dryRun) { map[issue.key] = num; save(); console.log(`${issue.key} → #${num}`); }
}

// Epic.
if (!map.EPIC) {
  const url = gh(['issue', 'create', '--title', 'Feature Audit 2026-10 — prioritized improvement roadmap (tracking epic)',
    '--body-file', writeBody('EPIC', epicBody(map)), '--label', AUDIT_LABEL, '--label', 'enhancement']);
  if (!dryRun) { map.EPIC = Number(url.split('/').pop()); save(); console.log(`EPIC → #${map.EPIC}`); }
}

// Pass 2: rewrite bodies so cross-references and the epic link resolve.
if (!process.argv.includes('--skip-relink')) {
  // --only KEY,KEY limits the relink to those issues and also syncs their titles.
  const onlyArg = process.argv.find(a => a.startsWith('--only='));
  const only = onlyArg ? new Set(onlyArg.slice('--only='.length).split(',')) : null;
  for (const issue of issues) {
    if (issue.advisory || (only && !only.has(issue.key))) continue;
    gh(['issue', 'edit', String(map[issue.key]), '--body-file', writeBody(`${issue.key}-final`, body(issue, map)),
      ...(only ? ['--title', issue.title] : [])]);
  }
  gh(['issue', 'edit', String(map.EPIC), '--body-file', writeBody('EPIC-final', epicBody(map))]);
}

writeFileSync(join(here, 'ISSUES.md'), issuesMarkdown(map));
console.log('Wrote ISSUES.md');
