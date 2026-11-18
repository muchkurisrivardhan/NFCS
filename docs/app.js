// NFC Desk website. No tracking, no third-party scripts.
'use strict';

const LINKS = {
  repo: 'muchkurisrivardhan/NFCS',
  sponsors: 'https://github.com/sponsors/muchkurisrivardhan',
  coffee: 'https://buymeacoffee.com/srivardhanmuchuri',
};

const $ = (s, el = document) => el.querySelector(s);
const $$ = (s, el = document) => [...el.querySelectorAll(s)];

// ---- download button: live once a GitHub release with NFCDesk.exe exists ----
fetch(`https://api.github.com/repos/${LINKS.repo}/releases/latest`)
  .then(r => (r.ok ? r.json() : null))
  .then(rel => {
    if (!rel) return;
    let found = false;
    for (const a of $$('.dl-link')) {
      const asset = (rel.assets || []).find(x => x.name === a.dataset.asset);
      if (!asset) continue;
      found = true;
      a.href = asset.browser_download_url;
      a.textContent = `Download ${rel.tag_name}`;
      a.classList.remove('btn-disabled');
      a.removeAttribute('aria-disabled');
    }
    if (!found) return;
    const note = $('#dl-note');
    note.textContent = '';
    note.append(`${rel.tag_name}, released ${new Date(rel.published_at).toLocaleDateString()}. Release notes are on `,
      Object.assign(document.createElement('a'), { href: rel.html_url, textContent: 'GitHub' }), '.');
  })
  .catch(() => {});

// ---- GitHub star count (shown only once there are stars) ----
fetch(`https://api.github.com/repos/${LINKS.repo}`)
  .then(r => (r.ok ? r.json() : null))
  .then(d => {
    if (!d || !d.stargazers_count) return;
    const n = d.stargazers_count;
    const el = $('#stars');
    el.textContent = n >= 1000 ? (n / 1000).toFixed(n >= 10000 ? 0 : 1) + 'k' : String(n);
    el.hidden = false;
  })
  .catch(() => {});

// ---- the agent clock ----
const clock = $('#clock');
const tick = () => { clock.textContent = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }); };
tick(); setInterval(tick, 15000);

// ================= tap simulator =================
// A faithful, in-browser model of the agent's checks (docs/PC-BRIDGE.md). Nothing is sent anywhere.

const hex = bytes => [...new Uint8Array(bytes)].map(b => b.toString(16).padStart(2, '0')).join('');
const randomSecret = () => hex(crypto.getRandomValues(new Uint8Array(32)));
async function sign(tag, ts, secret) {
  // Exactly what the shortcut does: SHA256 of the text "tag|ts|secret", as lowercase hex.
  return hex(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(`${tag}|${ts}|${secret}`)));
}

const SECRET = randomSecret();               // "paired" with this page's pretend phone
const REGISTERED = { 'desk-focus-01': { label: 'Focus mode', script: 'focus-mode.ps1' } };
const lastTs = {};                           // last accepted ts per tag, like config.json
let lastAccepted = null;                     // the previous accepted request, for the replay scenario
const now = () => Math.floor(Date.now() / 1000);

function lanAllowed(ip) {
  const p = ip.split('.').map(Number);
  return p[0] === 10 || p[0] === 127 || (p[0] === 172 && p[1] >= 16 && p[1] <= 31) || (p[0] === 192 && p[1] === 168);
}

async function buildRequest(scen) {
  const phone = '192.168.1.23';
  if (scen === 'replay') {
    if (!lastAccepted) {               // nothing to replay yet: record one real tap first, silently
      const first = await buildRequest('ok');
      await verify(first);
    }
    return { ...lastAccepted, note: 'Same bytes as the last accepted tap, sent again' };
  }
  let tag = 'desk-focus-01', ts = now(), src = phone, secret = SECRET, note = 'Signed by your paired iPhone';
  if (lastTs[tag] !== undefined && ts <= lastTs[tag]) ts = lastTs[tag] + 1;
  if (scen === 'sig') { secret = randomSecret(); note = 'Signed with a guessed secret'; }
  if (scen === 'skew') { ts = now() - 45; note = 'Captured 45 seconds ago'; }
  if (scen === 'wan') { src = '203.0.113.7'; note = 'Arrives from outside your network'; }
  if (scen === 'unknown') { tag = 'garage-door-01'; note = 'A tag the agent has not learned'; }
  return { src, tag, ts: String(ts), sig: await sign(tag, ts, secret), note };
}

// Returns one result per check: true (pass), false (fail, stop) — later checks are skipped.
async function verify(r) {
  const res = [];
  const step = ok => { res.push(ok); return ok; };
  if (!step(lanAllowed(r.src))) return res;
  if (!step(/^[a-z0-9-]{1,64}$/.test(r.tag) && /^(0|[1-9][0-9]{0,18})$/.test(r.ts))) return res;
  const ts = Number(r.ts);
  if (!step(Math.abs(now() - ts) <= 30)) return res;
  if (!step(lastTs[r.tag] === undefined || ts > lastTs[r.tag])) return res;
  if (!step((await sign(r.tag, r.ts, SECRET)) === r.sig.toLowerCase())) return res;
  if (!step(Boolean(REGISTERED[r.tag]))) return res;
  lastTs[r.tag] = ts;
  lastAccepted = { src: r.src, tag: r.tag, ts: r.ts, sig: r.sig };
  return res;
}

const reqEl = $('#req'), respEl = $('#resp'), actionEl = $('#action'), tapBtn = $('#tap');
const rows = $$('#checks li');
const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
const pause = ms => new Promise(r => setTimeout(r, reduce ? 0 : ms));
let scenario = 'ok';

function showRequest(r) {
  const esc = s => String(s).replace(/[&<>]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
  reqEl.innerHTML =
    `<span class="k">from</span> <b>${esc(r.src)}</b>  ·  ${esc(r.note)}\n` +
    `<span class="k">POST</span> /tap {"tag":"<b>${esc(r.tag)}</b>","ts":<b>${esc(r.ts)}</b>,\n` +
    `  "sig":"${esc(r.sig)}"}`;
}

async function runTap() {
  tapBtn.disabled = true;
  rows.forEach(li => { li.className = ''; $('.r', li).textContent = 'WAIT'; });
  respEl.className = 'resp'; respEl.textContent = 'Checking…'; actionEl.textContent = '';
  try {
    const r = await buildRequest(scenario);
    showRequest(r);
    const res = await verify(r);
    for (let i = 0; i < rows.length; i++) {
      await pause(170);
      const li = rows[i];
      if (i < res.length) { li.className = res[i] ? 'pass' : 'fail'; $('.r', li).textContent = res[i] ? 'PASS' : 'FAIL'; }
      else { li.className = 'skip'; $('.r', li).textContent = 'SKIP'; }
    }
    const ok = res.length === rows.length && res.every(Boolean);
    respEl.className = 'resp ' + (ok ? 'ok' : 'no');
    respEl.textContent = ok ? '200 {"status":"ok"}' : '403 {"status":"denied"}';
    const m = REGISTERED[r.tag];
    actionEl.textContent = ok ? `Ran: ${m.label} · ${m.script}` : 'Nothing runs';
  } catch {
    respEl.textContent = 'This browser can’t compute SHA-256 here';
  } finally {
    tapBtn.disabled = false;
  }
}

$$('.scen button').forEach(b => b.addEventListener('click', () => {
  scenario = b.dataset.scen;
  $$('.scen button').forEach(x => x.setAttribute('aria-checked', String(x === b)));
  runTap();
}));
tapBtn.addEventListener('click', runTap);

// ---- sponsor tabs and links ----
$$('.sp-tabs button').forEach(b => b.addEventListener('click', () => {
  $$('.sp-tabs button').forEach(x => x.setAttribute('aria-selected', String(x === b)));
  $$('.sp-panel').forEach(p => { p.hidden = p.dataset.panel !== b.dataset.tab; });
}));

let onceAmount = 10;
$$('.amt').forEach(b => b.addEventListener('click', () => {
  onceAmount = +b.dataset.amt;
  $$('.amt').forEach(x => x.setAttribute('aria-pressed', String(x === b)));
}));
$('.amt[data-amt="10"]').setAttribute('aria-pressed', 'true');

function hrefFor(kind, a) {
  if (kind === 'coffee') return LINKS.coffee;
  if (!LINKS.sponsors) return null;
  if (kind === 'sponsors-once') return `${LINKS.sponsors}?frequency=one-time&amount=${onceAmount}`;
  const amt = Math.max(1, Math.round(+(a && a.dataset.amt || $('#monthly-amount').value) || 5));
  return `${LINKS.sponsors}?frequency=recurring&amount=${amt}`;
}
let anyMissing = false;
$$('.sponsor-link').forEach(a => {
  const ready = Boolean(hrefFor(a.dataset.kind, a));
  if (!ready) {
    anyMissing = true;
    a.textContent = 'Coming soon';
    a.classList.add('btn-disabled');
    a.removeAttribute('href');
    a.setAttribute('aria-disabled', 'true');
    return;
  }
  a.target = '_blank'; a.rel = 'noopener';
  a.addEventListener('click', () => { a.href = hrefFor(a.dataset.kind, a); });
  a.href = hrefFor(a.dataset.kind, a);
});
$('#sp-soon').hidden = !anyMissing;
