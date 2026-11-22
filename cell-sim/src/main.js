// 세포 관리 시뮬레이션 v0.1 — 렌더링·UI. 시뮬레이션은 sim.js(고정 10 Hz)와 분리.
import * as S from './sim.js';

const $ = (id) => document.getElementById(id);
const cv = $('cv');
const ctx = cv.getContext('2d');
const FG = '#000', BG = '#fff', DIM = '#888';

const RES_LABEL = { glucose: '포도당', o2: '산소 O₂', amino: '아미노산', atp: 'ATP', pyruvate: '피루브산', protein: '단백질', waste: '폐기물' };
const BUILD_ORDER = ['transporter', 'respChain', 'ribosome', 'mitochondrion', 'lysosome', 'golgi'];
const IO = (d) => {
  const f = (o) => Object.entries(o).map(([k, v]) => `${RES_LABEL[k] || (k === '$substrate' ? '기질' : '막')} ${v}`).join(' + ');
  return `${f(d.in)} → ${f(d.out)}`;
};

let cfg, sim, paused = false, speed = 1, acc = 0, last = performance.now();
let placing = null, selected = null, hover = null, mouse = null;
let particles = [];
let atpHist = [];

async function boot() {
  try {
    cfg = await (await fetch('config.json')).json();
  } catch (e) {
    $('hint').textContent = 'config.json을 불러오지 못했습니다. 로컬 서버(예: python3 -m http.server)로 여세요.';
    return;
  }
  buildLegend();
  restart();
  requestAnimationFrame(frame);
}

function restart() {
  sim = S.createSim(cfg, { seed: (Math.random() * 1e9) | 0 });
  particles = []; selected = null; placing = null; atpHist = [];
  $('overlay').hidden = true;
  for (const o of sim.org) autoPlace(o);
}

// ---------- 좌표 ----------

function view() {
  const r = cv.getBoundingClientRect();
  const W = r.width, H = r.height;
  const base = Math.min(W, H) * 0.42;
  const scale = sim.stage === 'prokaryote' ? 0.45 + 0.18 * sim.size : 1;
  return { W, H, cx: W / 2, cy: H / 2, R: base * scale };
}

function slotOrgs() {
  return sim.org.filter((o) => cfg.organelles[o.type].membraneSlot);
}

function orgXY(o, v) {
  if (cfg.organelles[o.type].membraneSlot) {
    return { x: v.cx + Math.cos(o.angle) * v.R, y: v.cy + Math.sin(o.angle) * v.R };
  }
  return { x: v.cx + o.x * v.R, y: v.cy + o.y * v.R };
}

function autoPlace(o, at) {
  const def = cfg.organelles[o.type];
  if (def.membraneSlot) {
    if (at) { o.angle = Math.atan2(at.y, at.x); return; }
    const used = slotOrgs().filter((x) => x !== o && x.angle != null).map((x) => x.angle);
    let best = 0, bestGap = -1;
    for (let k = 0; k < 72; k++) {
      const a = (k / 72) * Math.PI * 2;
      const gap = used.length ? Math.min(...used.map((u) => Math.abs(Math.atan2(Math.sin(a - u), Math.cos(a - u))))) : 1;
      if (gap > bestGap) { bestGap = gap; best = a; }
    }
    o.angle = best;
    return;
  }
  if (o.type === 'nucleus') { o.x = 0; o.y = 0; return; }
  if (at) {
    const d = Math.hypot(at.x, at.y), m = 0.78;
    o.x = d > m ? (at.x / d) * m : at.x; o.y = d > m ? (at.y / d) * m : at.y;
    return;
  }
  for (let k = 0; k < 40; k++) {
    const a = Math.random() * Math.PI * 2, d = 0.3 + Math.random() * 0.45;
    const x = Math.cos(a) * d, y = Math.sin(a) * d;
    if (sim.org.every((p) => p === o || p.x == null || Math.hypot(p.x - x, p.y - y) > 0.18)) { o.x = x; o.y = y; return; }
  }
  o.x = 0.4; o.y = 0.4;
}

function toCell(px, py) {
  const v = view();
  return { x: (px - v.cx) / v.R, y: (py - v.cy) / v.R };
}

function pickOrg(px, py) {
  const v = view();
  let best = null, bd = 18;
  for (const o of sim.org) {
    const p = orgXY(o, v);
    const d = Math.hypot(p.x - px, p.y - py) - (o.type === 'nucleus' ? v.R * 0.16 : 0);
    if (d < bd) { bd = d; best = o; }
  }
  return best;
}

// ---------- 입력 ----------

cv.addEventListener('pointermove', (e) => {
  const r = cv.getBoundingClientRect();
  mouse = { x: e.clientX - r.left, y: e.clientY - r.top };
  hover = placing ? null : pickOrg(mouse.x, mouse.y);
});
cv.addEventListener('pointerleave', () => { mouse = null; hover = null; });
cv.addEventListener('click', (e) => {
  const r = cv.getBoundingClientRect();
  const px = e.clientX - r.left, py = e.clientY - r.top;
  if (placing) {
    const at = toCell(px, py);
    if (Math.hypot(at.x, at.y) > 1.25) return;
    const o = S.build(sim, placing, null);
    if (o) { autoPlace(o, at); selected = o.id; }
    if (!e.shiftKey || !S.canBuild(sim, placing).ok) setPlacing(null);
    return;
  }
  const o = pickOrg(px, py);
  selected = o ? o.id : null;
});
window.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') { setPlacing(null); selected = null; }
  if (e.key === ' ' && e.target === document.body) { e.preventDefault(); togglePause(); }
});

function setPlacing(type) {
  placing = type;
  cv.classList.toggle('placing', !!type);
}
function togglePause() {
  paused = !paused;
  $('pause').setAttribute('aria-pressed', paused);
  $('pause').textContent = paused ? '재개' : '일시정지';
}
$('pause').onclick = togglePause;
$('sp1').onclick = () => { speed = 1; $('sp1').setAttribute('aria-pressed', true); $('sp2').setAttribute('aria-pressed', false); };
$('sp2').onclick = () => { speed = 2; $('sp1').setAttribute('aria-pressed', false); $('sp2').setAttribute('aria-pressed', true); };
$('restart').onclick = restart;
$('ovRestart').onclick = restart;

// ---------- 루프 ----------

function frame(now) {
  const dtReal = Math.min(0.25, (now - last) / 1000);
  last = now;
  const tickDt = 1 / cfg.tickHz;
  if (!paused && sim.status === 'running') {
    acc += dtReal * speed;
    while (acc >= tickDt) {
      acc -= tickDt;
      const before = sim.org.length;
      S.step(sim);
      spawnParticles();
      if (sim.tick % cfg.tickHz === 0) {
        atpHist.push(sim.stats.atpMade);
        if (atpHist.length > 6) atpHist.shift();
      }
      if (sim.org.length !== before || sim.org.some((o) => o.angle == null && o.x == null)) {
        for (const o of sim.org) if (o.angle == null && o.x == null) autoPlace(o);
      }
    }
  }
  render(now / 1000, dtReal * (paused ? 0 : speed));
  if (sim.tick % 2 === 0 || paused) updatePanel();
  requestAnimationFrame(frame);
}

// ---------- 파티클 (자원 흐름 시각화) ----------

function anchor(ref, v) {
  if (typeof ref === 'number') {
    const o = sim.org.find((x) => x.id === ref);
    if (o) return orgXY(o, v);
  }
  const a = Math.random() * Math.PI * 2;
  if (ref === 'env') return { x: v.cx + Math.cos(a) * v.R * 1.35, y: v.cy + Math.sin(a) * v.R * 1.35 };
  if (ref === 'membrane') return { x: v.cx + Math.cos(a) * v.R, y: v.cy + Math.sin(a) * v.R };
  const d = Math.sqrt(Math.random()) * v.R * 0.7;
  return { x: v.cx + Math.cos(a) * d, y: v.cy + Math.sin(a) * d };
}

function spawnParticles() {
  const v = view();
  for (const f of sim.flows) {
    if (particles.length > 260) break;
    const a = anchor(f.src, v), b = anchor(f.dst, v);
    particles.push({ a, b, t: 0, res: f.res });
  }
}

function drawToken(x, y, res, g = ctx) {
  g.beginPath();
  switch (res) {
    case 'atp': g.arc(x, y, 2.2, 0, 7); g.fill(); break;
    case 'o2': g.arc(x, y, 2.6, 0, 7); g.stroke(); break;
    case 'glucose': g.rect(x - 2.4, y - 2.4, 4.8, 4.8); g.stroke(); break;
    case 'amino': g.moveTo(x, y - 3); g.lineTo(x + 3, y + 2.4); g.lineTo(x - 3, y + 2.4); g.closePath(); g.stroke(); break;
    case 'protein': g.moveTo(x, y - 3.2); g.lineTo(x + 3.2, y); g.lineTo(x, y + 3.2); g.lineTo(x - 3.2, y); g.closePath(); g.fill(); break;
    case 'pyruvate': g.rect(x - 1.6, y - 1.6, 3.2, 3.2); g.fill(); break;
    case 'waste': g.moveTo(x - 2.5, y - 2.5); g.lineTo(x + 2.5, y + 2.5); g.moveTo(x + 2.5, y - 2.5); g.lineTo(x - 2.5, y + 2.5); g.stroke(); break;
  }
}

function buildLegend() {
  const el = $('legend');
  for (const r of S.RES) {
    const c = document.createElement('canvas');
    c.width = 24; c.height = 24; c.style.width = '12px'; c.style.height = '12px'; c.style.verticalAlign = '-2px';
    const g = c.getContext('2d');
    g.scale(2, 2); g.strokeStyle = FG; g.fillStyle = FG; g.lineWidth = 1;
    drawToken(6, 6, r, g);
    const span = document.createElement('span');
    span.append(c, ' ' + RES_LABEL[r]);
    el.append(span);
  }
}

// ---------- 렌더 ----------

function render(t, dtSim) {
  const dpr = window.devicePixelRatio || 1;
  const rect = cv.getBoundingClientRect();
  if (cv.width !== Math.round(rect.width * dpr) || cv.height !== Math.round(rect.height * dpr)) {
    cv.width = Math.round(rect.width * dpr); cv.height = Math.round(rect.height * dpr);
  }
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.fillStyle = BG; ctx.fillRect(0, 0, rect.width, rect.height);
  const v = view();
  ctx.strokeStyle = FG; ctx.fillStyle = FG; ctx.lineWidth = 1;
  ctx.font = '11px "IBM Plex Sans KR", system-ui, sans-serif';
  ctx.textAlign = 'center'; ctx.textBaseline = 'middle';

  // 환경 O₂ 농도: 바깥 점묘 밀도
  ctx.fillStyle = DIM;
  const dots = Math.round(60 * sim.env.o2);
  for (let i = 0; i < dots; i++) {
    const a = i * 2.39996 + t * 0.02, d = v.R * (1.12 + ((i * 37) % 50) / 100);
    ctx.fillRect(v.cx + Math.cos(a) * d, v.cy + Math.sin(a) * d, 1.2, 1.2);
  }
  ctx.fillStyle = FG;

  // 세포막
  const m = sim.membrane / cfg.homeostasis.membraneMax;
  ctx.lineWidth = 1 + 4 * m;
  if (sim.membrane < cfg.homeostasis.membraneCritical) ctx.setLineDash([6, 5]);
  ctx.beginPath(); ctx.arc(v.cx, v.cy, v.R, 0, Math.PI * 2); ctx.stroke();
  ctx.setLineDash([]);
  if (sim.stage === 'prokaryote') {
    // 핵양체: 불규칙 루프
    ctx.lineWidth = 1; ctx.beginPath();
    for (let k = 0; k <= 60; k++) {
      const a = (k / 60) * Math.PI * 2, rr = v.R * (0.16 + 0.04 * Math.sin(a * 5 + t * 0.3));
      const x = v.cx + Math.cos(a) * rr, y = v.cy + Math.sin(a) * rr * 0.7;
      k ? ctx.lineTo(x, y) : ctx.moveTo(x, y);
    }
    ctx.stroke(); ctx.fillText('핵양체', v.cx, v.cy);
  }

  // 공생 진행
  if (sim.symbiosis) {
    const p = sim.symbiosis.elapsed / sim.symbiosis.duration;
    const bx = v.cx + v.R * 0.45 * Math.cos(t * 0.5), by = v.cy + v.R * 0.45 * Math.sin(t * 0.5);
    ctx.lineWidth = 1; ctx.beginPath(); ctx.ellipse(bx, by, 16, 9, t * 0.5, 0, 7); ctx.stroke();
    ctx.lineWidth = 3; ctx.beginPath(); ctx.arc(bx, by, 22, -Math.PI / 2, -Math.PI / 2 + p * Math.PI * 2); ctx.stroke();
    ctx.lineWidth = 1; ctx.fillText(`공생 ${Math.round(p * 100)}%`, bx, by + 34);
  }

  // 소기관
  for (const o of sim.org) drawOrg(o, orgXY(o, v), v, t);

  // 바이러스
  ctx.lineWidth = 1;
  for (let i = 0; i < sim.virus.outside; i++) {
    const a = t * 0.4 + (i / Math.max(1, sim.virus.outside)) * Math.PI * 2;
    hex(v.cx + Math.cos(a) * v.R * 1.12, v.cy + Math.sin(a) * v.R * 1.12, 6);
  }

  // 파티클
  ctx.lineWidth = 1;
  particles = particles.filter((p) => (p.t += dtSim / 0.8) < 1);
  for (const p of particles) {
    const e = p.t * p.t * (3 - 2 * p.t);
    drawToken(p.a.x + (p.b.x - p.a.x) * e, p.a.y + (p.b.y - p.a.y) * e, p.res);
  }

  // 배치 고스트
  if (placing && mouse) {
    const def = cfg.organelles[placing];
    let p = mouse;
    if (def.membraneSlot) {
      const a = Math.atan2(mouse.y - v.cy, mouse.x - v.cx);
      p = { x: v.cx + Math.cos(a) * v.R, y: v.cy + Math.sin(a) * v.R };
    }
    ctx.globalAlpha = 0.45;
    drawOrg({ type: placing, id: -1, hp: 100, angle: 0, mode: 'glucose' }, p, v, t);
    ctx.globalAlpha = 1;
  }

  // 호버 라벨
  const lab = hover || (selected && sim.org.find((o) => o.id === selected));
  if (lab) {
    const p = orgXY(lab, v);
    const txt = cfg.organelles[lab.type].label + (lab.mode ? ` · ${RES_LABEL[lab.mode]}` : '');
    const w = ctx.measureText(txt).width + 10;
    ctx.fillStyle = BG; ctx.fillRect(p.x - w / 2, p.y - 34, w, 16);
    ctx.strokeStyle = FG; ctx.lineWidth = 1; ctx.strokeRect(p.x - w / 2, p.y - 34, w, 16);
    ctx.fillStyle = FG; ctx.fillText(txt, p.x, p.y - 26);
  }
}

function hex(x, y, r) {
  ctx.beginPath();
  for (let k = 0; k < 6; k++) {
    const a = (k / 6) * Math.PI * 2;
    k ? ctx.lineTo(x + Math.cos(a) * r, y + Math.sin(a) * r) : ctx.moveTo(x + r, y);
  }
  ctx.closePath(); ctx.stroke();
  ctx.beginPath(); ctx.moveTo(x, y + r); ctx.lineTo(x, y + r + 4); ctx.stroke();
}

function drawOrg(o, p, v, t) {
  const { x, y } = p;
  ctx.save();
  ctx.lineWidth = 1.2;
  ctx.strokeStyle = FG; ctx.fillStyle = FG;
  if (o.hp < 100) ctx.setLineDash([3, 2 + (100 - o.hp) / 15]);
  const s = Math.max(0.75, Math.min(1.2, v.R / 220));
  switch (o.type) {
    case 'nucleus': {
      const r = v.R * 0.17;
      ctx.beginPath(); ctx.arc(x, y, r, 0, 7); ctx.stroke();
      ctx.beginPath(); ctx.arc(x, y, r - 4, 0, 7); ctx.stroke();
      ctx.setLineDash([]); ctx.fillText('핵', x, y);
      break;
    }
    case 'transporter': {
      ctx.translate(x, y); ctx.rotate(o.angle);
      ctx.fillStyle = BG; ctx.fillRect(-9 * s, -5 * s, 18 * s, 10 * s);
      ctx.strokeRect(-9 * s, -5 * s, 18 * s, 10 * s);
      ctx.beginPath(); ctx.moveTo(-9 * s, 0); ctx.lineTo(9 * s, 0); ctx.stroke();
      ctx.rotate(-o.angle); ctx.fillStyle = FG;
      ctx.fillText(o.mode === 'amino' ? 'A' : 'G', Math.cos(o.angle) * -16, Math.sin(o.angle) * -16);
      break;
    }
    case 'respChain': {
      ctx.translate(x, y); ctx.rotate(o.angle + Math.PI / 2);
      ctx.fillStyle = BG;
      ctx.beginPath(); ctx.moveTo(0, -8 * s); ctx.lineTo(8 * s, 6 * s); ctx.lineTo(-8 * s, 6 * s); ctx.closePath(); ctx.fill(); ctx.stroke();
      ctx.fillStyle = FG; ctx.beginPath(); ctx.arc(0, 1 * s, 2, 0, 7); ctx.fill();
      break;
    }
    case 'ribosome': {
      ctx.beginPath(); ctx.arc(x - 3 * s, y, 4.5 * s, 0, 7); ctx.fill();
      ctx.beginPath(); ctx.arc(x + 4 * s, y + 1, 3.5 * s, 0, 7); ctx.fill();
      if (o.hijacked) {
        ctx.setLineDash([]); ctx.lineWidth = 1;
        const hx = x, hy = y - 14;
        ctx.beginPath();
        for (let k = 0; k < 6; k++) { const a = (k / 6) * Math.PI * 2; k ? ctx.lineTo(hx + Math.cos(a) * 5, hy + Math.sin(a) * 5) : ctx.moveTo(hx + 5, hy); }
        ctx.closePath(); ctx.stroke();
      }
      break;
    }
    case 'mitochondrion': {
      const rx = 22 * s, ry = 11 * s, rot = (o.id * 1.7) % Math.PI;
      ctx.translate(x, y); ctx.rotate(rot);
      ctx.fillStyle = BG; ctx.beginPath(); ctx.ellipse(0, 0, rx, ry, 0, 0, 7); ctx.fill(); ctx.stroke();
      ctx.beginPath();
      for (let k = 0; k <= 8; k++) {
        const px = -rx * 0.7 + (k / 8) * rx * 1.4, py = (k % 2 ? 1 : -1) * ry * 0.55;
        k ? ctx.lineTo(px, py) : ctx.moveTo(px, py);
      }
      ctx.stroke();
      break;
    }
    case 'lysosome': {
      const r = 10 * s;
      ctx.fillStyle = BG; ctx.beginPath(); ctx.arc(x, y, r, 0, 7); ctx.fill(); ctx.stroke();
      ctx.fillStyle = FG;
      for (let k = 0; k < 5; k++) { const a = k * 1.26 + t * 0.2; ctx.fillRect(x + Math.cos(a) * r * 0.5 - 1, y + Math.sin(a) * r * 0.5 - 1, 2, 2); }
      break;
    }
    case 'golgi': {
      ctx.translate(x, y);
      for (let k = 0; k < 4; k++) {
        ctx.beginPath(); ctx.arc(0, 18 * s + k * 5 * s, 18 * s, Math.PI * 1.22, Math.PI * 1.78); ctx.stroke();
      }
      break;
    }
  }
  ctx.restore();
  if (o.id === selected) {
    ctx.save(); ctx.setLineDash([2, 3]); ctx.lineWidth = 1;
    const r = o.type === 'nucleus' ? v.R * 0.17 + 8 : 20;
    ctx.beginPath(); ctx.arc(x, y, r, 0, 7); ctx.stroke(); ctx.restore();
  }
}

// ---------- 패널 ----------

const fmt = (sec) => `${String(Math.floor(sec / 60)).padStart(2, '0')}:${String(Math.floor(sec % 60)).padStart(2, '0')}`;

function bar(ratio, mark) {
  return `<div class="bar"><i style="width:${Math.max(0, Math.min(1, ratio)) * 100}%"></i>${mark != null ? `<b style="left:${mark * 100}%"></b>` : ''}</div>`;
}

let lastBuildKey = '';
function updatePanel() {
  const H = cfg.homeostasis;
  $('clock').textContent = fmt(sim.t);
  $('stageLabel').textContent = sim.stage === 'prokaryote' ? `원핵세포 · 크기 ${sim.size}/${cfg.prokaryote.maxSize}` : '진핵세포';

  // 위협
  const th = $('threat');
  const parts = [];
  for (const a of sim.threat.active) parts.push(`<strong>${cfg.threats[a.type].label}</strong> 진행 중 · ${Math.ceil(a.until - sim.t)}초 남음`);
  const p = sim.threat.pending;
  if (p && p.warned) parts.push(`경고: <strong>${cfg.threats[p.type].label}</strong> ${Math.ceil(p.at - sim.t)}초 후`);
  if (sim.virus.inside) parts.push(`세포 내 바이러스 ${sim.virus.inside} (리보솜 탈취)`);
  th.innerHTML = parts.length ? parts.join('<br>') : '위협 없음';
  th.classList.toggle('alarm', parts.length > 0);

  // 항상성
  const wr = S.wasteRatio(sim);
  const hrows = [
    ['ATP 잔량', sim.res.atp / cfg.caps.atp, null, `${sim.res.atp}`, sim.crit.atp],
    ['폐기물', wr, H.wasteCriticalRatio, `${Math.round(wr * 100)}%`, sim.crit.waste],
    ['세포막', sim.membrane / H.membraneMax, H.membraneCritical / H.membraneMax, `${Math.round(sim.membrane)}%`, sim.crit.membrane],
  ];
  $('homeo').innerHTML = hrows.map(([n, r, mk, val, c]) =>
    `<div class="row"><span class="${c > 0 ? 'crit' : ''}">${n}</span>${bar(r, mk)}<span class="val mono">${c > 0 ? `${Math.floor(c)}/${H.criticalSec}s` : val}</span></div>`).join('');

  // 자원
  $('res').innerHTML = S.RES.map((r) =>
    `<div class="row"><span>${RES_LABEL[r]}</span>${bar(sim.res[r] / cfg.caps[r])}<span class="val mono">${sim.res[r]}/${cfg.caps[r]}</span></div>`).join('')
    + (sim.fermenting ? '<div style="font-size:11px">발효 중: 피루브산 → 폐기물</div>' : '');

  // 건설
  const slots = S.membraneSlots(sim);
  $('slots').textContent = Number.isFinite(slots) ? `막 슬롯 ${S.usedSlots(sim)}/${slots}` : '';
  const types = BUILD_ORDER.filter((k) => cfg.organelles[k].stages.includes(sim.stage));
  const key = types.map((k) => `${k}:${S.canBuild(sim, k).ok}:${S.count(sim, k)}:${placing === k}`).join('|');
  if (key !== lastBuildKey) {
    lastBuildKey = key;
    const box = $('build'); box.innerHTML = '';
    for (const k of types) {
      const d = cfg.organelles[k], c = S.canBuild(sim, k);
      const b = document.createElement('button');
      b.disabled = !c.ok && placing !== k;
      b.setAttribute('aria-pressed', placing === k);
      b.innerHTML = `<span>${d.label} <span class="mono">×${S.count(sim, k)}</span></span><span class="mono">단백질 ${d.cost}</span><small>${IO(d)} · 유지 ${d.upkeep}/s${c.ok ? '' : ' · ' + c.why}</small>`;
      b.onclick = () => setPlacing(placing === k ? null : k);
      box.append(b);
    }
  }

  // 세포 행동
  const act = $('actions');
  const specs = [];
  const mk = (label, check, fn) => specs.push({ text: label + (check.ok ? '' : ` (${check.why})`), dis: !check.ok || sim.status !== 'running', fn });
  if (sim.stage === 'prokaryote') {
    const g = S.canGrow(sim);
    mk(`성장${g.ok ? ` · 단백질 ${g.cost}` : ''}`, g, () => S.grow(sim));
    const sy = cfg.prokaryote.symbiosis;
    mk(`공생 포획 · 단백질 ${sy.proteinCost}, ATP ${sy.atpCost}`, S.canSymbiosis(sim), () => S.startSymbiosis(sim));
  } else {
    mk(`세포분열 · 단백질 ${cfg.win.proteinForDivision}`, S.canDivide(sim), () => S.divide(sim));
  }
  const actKey = specs.map((x) => x.text + x.dis).join('|');
  if (act.dataset.k !== actKey) {
    act.dataset.k = actKey; act.innerHTML = '';
    for (const x of specs) {
      const b = document.createElement('button');
      b.textContent = x.text; b.disabled = x.dis; b.onclick = x.fn; act.append(b);
    }
  }
  const made = atpHist.length > 1 ? (atpHist.at(-1) - atpHist[0]) / (atpHist.length - 1) : 0;
  $('econ').textContent = `ATP 생산 ${made.toFixed(1)}/s · 유지비 ${S.totalUpkeep(sim).toFixed(1)}/s` + (sim.stage === 'prokaryote' ? ` (부피 ${(cfg.prokaryote.upkeepPerVolume * sim.size ** 3).toFixed(1)})` : '');

  // 선택
  const o = selected && sim.org.find((x) => x.id === selected);
  $('selBox').hidden = !o;
  if (o) {
    const d = cfg.organelles[o.type];
    const sel = $('sel');
    const k = `${o.id}:${o.mode}:${Math.round(o.hp)}:${o.hijacked}`;
    if (sel.dataset.k !== k) {
      sel.dataset.k = k;
      sel.innerHTML = `<div><strong>${d.label}</strong> <span class="mono">#${o.id} · 상태 ${Math.round(o.hp)}%</span></div>
        <div class="mono" style="font-size:11px">${IO(d)} · ${d.rate}/s · 유지 ${d.upkeep}/s${o.hijacked ? ' · 바이러스 탈취됨' : ''}</div>
        <div class="actions" style="margin-top:6px"></div>`;
      const a = sel.querySelector('.actions');
      if (o.type === 'transporter') {
        for (const m of ['glucose', 'amino']) {
          const b = document.createElement('button');
          b.textContent = RES_LABEL[m]; b.setAttribute('aria-pressed', o.mode === m);
          b.onclick = () => S.setMode(sim, o.id, m);
          a.append(b);
        }
      }
      if (!d.fixed) {
        const b = document.createElement('button');
        b.textContent = `해체 (단백질 +${Math.floor(d.cost / 2)})`;
        b.onclick = () => { S.demolish(sim, o.id); selected = null; };
        a.append(b);
      }
    }
  }

  // 기록
  const logKey = sim.log.length + ':' + (sim.log.at(-1)?.t ?? 0);
  if ($('log').dataset.k !== logKey) {
    $('log').dataset.k = logKey;
    $('log').innerHTML = sim.log.slice(-12).reverse().map((l) => `<div>${fmt(l.t)} ${l.msg}</div>`).join('');
  }

  // 종료
  if (sim.status !== 'running' && $('overlay').hidden) {
    $('overlay').hidden = false;
    $('ovTitle').textContent = sim.status === 'won' ? '세포분열 완료' : '세포 사멸';
    $('ovReason').textContent = sim.reason;
    $('ovStats').textContent = `${fmt(sim.t)} · 누적 ATP ${sim.stats.atpMade} · 단백질 ${sim.stats.proteinMade}`;
  }
}

boot();
