// 세포 관리 시뮬레이션 v0.3 — 그리드 렌더링·입력·패널. 시뮬레이션은 sim.js.
import * as S from './sim.js';
import { Space } from './space.js';

const space = new Space();

const $ = (id) => document.getElementById(id);
const cv = $('cv');
const ctx = cv.getContext('2d');
const FG = '#000', BG = '#fff', GRID = '#e4e4e4', DIM = '#8a8a8a', CHEV = '#b5b5b5';

const ITEM_LABEL = { glucose: '포도당', amino: '아미노산', pyruvate: '피루브산', protein: '단백질', waste: '폐기물' };

let cfg, sim;
let paused = false, speed = 1, acc = 0, last = performance.now();
let tool = null;          // 건물 타입 | 'remove' | null
let toolDir = 0;          // 미세소관 방향
let hoverTile = null;
let selectedId = null;
let drag = null;
let viewR = 7;            // 화면 반지름(타일), 성장 시 부드럽게 보간
let atpHist = [];
let lastHotKey = '';

async function boot() {
  try {
    cfg = await (await fetch('config.json')).json();
  } catch {
    $('hint').textContent = 'config.json을 불러오지 못했습니다. 로컬 서버(python3 -m http.server)로 여세요.';
    return;
  }
  buildLegend();
  restart();
  requestAnimationFrame(frame);
}

function restart() {
  sim = S.createSim(cfg, { seed: (Math.random() * 1e9) | 0 });
  selectedId = null; tool = null; atpHist = []; drag = null;
  viewR = S.radius(sim) + 2;
  $('overlay').hidden = true;
  lastHotKey = '';
}

// ---------- 좌표 ----------

function layout() {
  const r = cv.getBoundingClientRect();
  const T = Math.max(8, Math.floor(Math.min(r.width, r.height) / (2 * viewR + 1)));
  const [bx, by] = space.bob();
  return { W: r.width, H: r.height, T, cx: Math.round(r.width / 2 + bx), cy: Math.round(r.height / 2 + by) };
}
const tileCenter = (L, x, y) => [L.cx + x * L.T, L.cy + y * L.T];
function screenToTile(px, py) {
  const L = layout();
  return { x: Math.round((px - L.cx) / L.T), y: Math.round((py - L.cy) / L.T) };
}

// ---------- 입력 ----------

const toolType = () => (tool && tool !== 'remove' ? tool : null);

function act(x, y, button) {
  if (button === 2 || tool === 'remove') {
    const e = S.entityAt(sim, x, y);
    if (e && e.id === selectedId) selectedId = null;
    S.remove(sim, x, y);
    return;
  }
  const t = toolType();
  if (t) {
    const [w, h] = S.dims(sim, t);
    const e = S.place(sim, t, x - Math.floor((w - 1) / 2), y - Math.floor((h - 1) / 2), toolDir);
    if (e && t !== 'belt') selectedId = e.id;
    return;
  }
  const e = S.entityAt(sim, x, y);
  selectedId = e ? e.id : null;
}

cv.addEventListener('contextmenu', (e) => e.preventDefault());
cv.addEventListener('pointerdown', (ev) => {
  cv.setPointerCapture(ev.pointerId);
  const r = cv.getBoundingClientRect();
  const t = screenToTile(ev.clientX - r.left, ev.clientY - r.top);
  drag = { last: t, button: ev.button };
  act(t.x, t.y, ev.button);
});
cv.addEventListener('pointermove', (ev) => {
  const r = cv.getBoundingClientRect();
  const t = screenToTile(ev.clientX - r.left, ev.clientY - r.top);
  hoverTile = t;
  if (!drag || (t.x === drag.last.x && t.y === drag.last.y)) return;
  // 드래그: 미세소관은 이동 방향을 따라 깔고 직전 타일 방향도 맞춘다
  let cur = drag.last;
  while (cur.x !== t.x || cur.y !== t.y) {
    const dx = Math.sign(t.x - cur.x), dy = dx ? 0 : Math.sign(t.y - cur.y);
    const next = { x: cur.x + dx, y: cur.y + dy };
    if (tool === 'belt' && drag.button === 0) {
      toolDir = S.DIRS.findIndex(([a, b]) => a === dx && b === dy);
      const prev = S.entityAt(sim, cur.x, cur.y);
      if (prev && prev.type === 'belt') prev.dir = toolDir;
      act(next.x, next.y, 0);
    } else if (drag.button === 2 || tool === 'remove') {
      act(next.x, next.y, 2);
    }
    cur = next;
  }
  drag.last = t;
});
cv.addEventListener('pointerup', () => { drag = null; });
cv.addEventListener('pointerleave', () => { hoverTile = null; });

function setTool(t) {
  tool = tool === t ? null : t;
  cv.style.cursor = tool ? 'crosshair' : 'default';
  lastHotKey = '';
}

window.addEventListener('keydown', (e) => {
  const k = e.key.toLowerCase();
  if (k === 'escape') { tool = null; setTool(null); selectedId = null; }
  else if (k === ' ') { e.preventDefault(); togglePause(); }
  else if (k === 'r') {
    const h = hoverTile && S.entityAt(sim, hoverTile.x, hoverTile.y);
    if (!toolType() && h && (h.type === 'belt' || h.type === 'filter')) S.rotate(sim, h.x, h.y);
    else { toolDir = (toolDir + 1) % 4; lastHotKey = ''; }
  } else if (k === 'x') setTool('remove');
  else {
    const t = Object.entries(cfg.buildings).find(([, d]) => d.key === k && d.stages.includes(sim.stage));
    if (t) setTool(t[0]);
  }
});

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
  const dt = Math.min(0.25, (now - last) / 1000);
  last = now;
  if (!paused && sim.status === 'running') {
    acc += dt * speed;
    const tickDt = 1 / cfg.tickHz;
    while (acc >= tickDt) {
      acc -= tickDt;
      S.step(sim);
      if (sim.tick % cfg.tickHz === 0) { atpHist.push(sim.stats.atpMade); if (atpHist.length > 6) atpHist.shift(); }
    }
  }
  if (!paused && sim.status === 'running') space.update(dt * speed);
  viewR += (S.radius(sim) + 2 - viewR) * Math.min(1, dt * 3);
  render(now / 1000);
  updatePanel();
  requestAnimationFrame(frame);
}

// ---------- 렌더 ----------

function drawToken(g, x, y, item, r) {
  g.beginPath();
  switch (item) {
    case 'glucose': g.rect(x - r, y - r, 2 * r, 2 * r); g.fillStyle = BG; g.fill(); g.stroke(); break;
    case 'amino': g.moveTo(x, y - r * 1.15); g.lineTo(x + r, y + r * 0.85); g.lineTo(x - r, y + r * 0.85); g.closePath(); g.fillStyle = BG; g.fill(); g.stroke(); break;
    case 'pyruvate': g.arc(x, y, r, 0, 7); g.fillStyle = BG; g.fill(); g.stroke(); g.beginPath(); g.arc(x, y, r * 0.4, 0, 7); g.fillStyle = FG; g.fill(); break;
    case 'protein': g.moveTo(x, y - r * 1.2); g.lineTo(x + r * 1.2, y); g.lineTo(x, y + r * 1.2); g.lineTo(x - r * 1.2, y); g.closePath(); g.fillStyle = FG; g.fill(); break;
    case 'waste': g.arc(x, y, r, 0, 7); g.fillStyle = BG; g.fill(); g.stroke(); g.beginPath(); g.moveTo(x - r * 0.6, y - r * 0.6); g.lineTo(x + r * 0.6, y + r * 0.6); g.moveTo(x + r * 0.6, y - r * 0.6); g.lineTo(x - r * 0.6, y + r * 0.6); g.stroke(); break;
  }
  g.fillStyle = FG;
}

function buildLegend() {
  const el = $('legend');
  for (const it of S.ITEMS) {
    const c = document.createElement('canvas');
    c.width = 28; c.height = 28; c.style.width = '14px'; c.style.height = '14px'; c.style.verticalAlign = '-3px';
    const g = c.getContext('2d');
    g.scale(2, 2); g.strokeStyle = FG; g.lineWidth = 1;
    drawToken(g, 7, 7, it, 4);
    const span = document.createElement('span');
    span.append(c, ' ' + ITEM_LABEL[it]);
    el.append(span);
  }
}

function render(t) {
  const dpr = window.devicePixelRatio || 1;
  const rect = cv.getBoundingClientRect();
  if (cv.width !== Math.round(rect.width * dpr) || cv.height !== Math.round(rect.height * dpr)) {
    cv.width = Math.round(rect.width * dpr); cv.height = Math.round(rect.height * dpr);
  }
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.fillStyle = BG; ctx.fillRect(0, 0, rect.width, rect.height);
  const L = layout(), T = L.T;
  const ext = Math.ceil(Math.max(L.W, L.H) / T / 2) + 1;
  const R = S.radius(sim);

  // 배경 공간 → 세포 내부를 덮고 격자
  space.resize(L.W, L.H);
  space.draw(ctx, { o2: sim.env.o2, glucose: sim.env.glucose, virus: sim.threat.active.some((a) => a.type === 'virus') });
  ctx.fillStyle = BG;
  ctx.beginPath(); ctx.arc(L.cx, L.cy, R * T, 0, Math.PI * 2); ctx.fill();
  ctx.lineWidth = 1; ctx.strokeStyle = GRID;
  for (let x = -ext; x <= ext; x++) for (let y = -ext; y <= ext; y++) {
    if (S.tileKind(sim, x, y) !== 'interior') continue;
    const [px, py] = tileCenter(L, x, y);
    ctx.strokeRect(px - T / 2 + 0.5, py - T / 2 + 0.5, T - 1, T - 1);
  }
  const tt = toolType();
  if (tt && S.def(sim, tt).place === 'membrane') {
    ctx.strokeStyle = DIM; ctx.setLineDash([2, 2]);
    for (let x = -R - 1; x <= R + 1; x++) for (let y = -R - 1; y <= R + 1; y++) {
      if (S.tileKind(sim, x, y) !== 'membrane' || S.entityAt(sim, x, y)) continue;
      const [px, py] = tileCenter(L, x, y);
      ctx.strokeRect(px - T / 2 + 2, py - T / 2 + 2, T - 4, T - 4);
    }
    ctx.setLineDash([]);
  }

  // 세포막
  ctx.strokeStyle = FG; ctx.lineWidth = 1 + 4 * (sim.membrane / cfg.homeostasis.membraneMax);
  if (sim.membrane < cfg.homeostasis.membraneCritical) ctx.setLineDash([8, 6]);
  ctx.beginPath(); ctx.arc(L.cx, L.cy, R * T, 0, Math.PI * 2); ctx.stroke();
  ctx.setLineDash([]);

  ctx.lineWidth = 1;
  for (const e of sim.ents.values()) if (e.type === 'belt') drawBelt(e, L, t);
  for (const e of sim.ents.values()) if (e.type !== 'belt') drawMachine(e, L);
  for (const e of sim.ents.values()) if (e.type === 'belt') drawBeltItems(e, L);

  // 바이러스
  ctx.strokeStyle = FG; ctx.lineWidth = 1;
  for (let i = 0; i < sim.virus.outside; i++) {
    const a = t * 0.35 + (i / sim.virus.outside) * Math.PI * 2;
    hex(L.cx + Math.cos(a) * (R + 1.3) * T, L.cy + Math.sin(a) * (R + 1.3) * T, T * 0.3);
  }

  // 공생 진행 링
  if (sim.symbiosis) {
    const p = sim.symbiosis.elapsed / sim.symbiosis.duration;
    ctx.lineWidth = 4;
    ctx.beginPath(); ctx.arc(L.cx, L.cy, T * 2.4, -Math.PI / 2, -Math.PI / 2 + p * Math.PI * 2); ctx.stroke();
    ctx.lineWidth = 1;
  }

  // 배치 미리보기
  if (hoverTile) {
    if (tt) {
      const [w, h] = S.dims(sim, tt);
      const x0 = hoverTile.x - Math.floor((w - 1) / 2), y0 = hoverTile.y - Math.floor((h - 1) / 2);
      const c = S.canPlace(sim, tt, x0, y0);
      const ok = c.ok || (tt === 'belt' && S.entityAt(sim, hoverTile.x, hoverTile.y)?.type === 'belt');
      const [px, py] = tileCenter(L, x0, y0);
      ctx.strokeStyle = FG; ctx.lineWidth = 2;
      if (!ok) ctx.setLineDash([4, 3]);
      ctx.strokeRect(px - T / 2 + 1, py - T / 2 + 1, w * T - 2, h * T - 2);
      ctx.setLineDash([]); ctx.lineWidth = 1.5;
      if (tt === 'belt' || tt === 'filter') arrow(px, py, toolDir, T * 0.32);
      ctx.lineWidth = 1;
      if (!ok) label(c.why, px + (w - 1) * T / 2, py - T * 0.95);
    } else {
      const [px, py] = tileCenter(L, hoverTile.x, hoverTile.y);
      const rm = tool === 'remove';
      ctx.strokeStyle = rm ? FG : DIM; ctx.lineWidth = rm ? 2 : 1;
      ctx.strokeRect(px - T / 2 + 1, py - T / 2 + 1, T - 2, T - 2);
      if (rm) { ctx.beginPath(); ctx.moveTo(px - T / 4, py - T / 4); ctx.lineTo(px + T / 4, py + T / 4); ctx.moveTo(px + T / 4, py - T / 4); ctx.lineTo(px - T / 4, py + T / 4); ctx.stroke(); }
      ctx.lineWidth = 1;
    }
  }
}

function label(text, x, y) {
  ctx.font = '11px "IBM Plex Sans KR", system-ui, sans-serif';
  ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
  const w = ctx.measureText(text).width + 10;
  ctx.fillStyle = BG; ctx.fillRect(x - w / 2, y - 8, w, 16);
  ctx.strokeStyle = FG; ctx.lineWidth = 1; ctx.strokeRect(x - w / 2, y - 8, w, 16);
  ctx.fillStyle = FG; ctx.fillText(text, x, y);
}

function arrow(px, py, dir, r) {
  const [dx, dy] = S.DIRS[dir];
  const nx = -dy, ny = dx;
  ctx.beginPath();
  ctx.moveTo(px + dx * r, py + dy * r);
  ctx.lineTo(px - dx * r * 0.2 + nx * r * 0.7, py - dy * r * 0.2 + ny * r * 0.7);
  ctx.moveTo(px + dx * r, py + dy * r);
  ctx.lineTo(px - dx * r * 0.2 - nx * r * 0.7, py - dy * r * 0.2 - ny * r * 0.7);
  ctx.stroke();
}

function hex(x, y, r) {
  ctx.beginPath();
  for (let k = 0; k < 6; k++) { const a = (k / 6) * Math.PI * 2; k ? ctx.lineTo(x + Math.cos(a) * r, y + Math.sin(a) * r) : ctx.moveTo(x + r, y); }
  ctx.closePath(); ctx.stroke();
}

function drawBelt(b, L, t) {
  const T = L.T;
  const [px, py] = tileCenter(L, b.x, b.y);
  const [dx, dy] = S.DIRS[b.dir];
  const nx = -dy, ny = dx, half = T * 0.3;
  ctx.strokeStyle = FG; ctx.lineWidth = 1;
  ctx.beginPath();
  for (const sg of [-1, 1]) {
    ctx.moveTo(px - dx * T / 2 + nx * half * sg, py - dy * T / 2 + ny * half * sg);
    ctx.lineTo(px + dx * T / 2 + nx * half * sg, py + dy * T / 2 + ny * half * sg);
  }
  ctx.stroke();
  // 흐름 셰브론: 정체 시 멈춘다
  const phase = b.blocked ? 0.25 : (t * S.beltSpeed(sim)) % 1;
  ctx.strokeStyle = CHEV;
  for (const k of [0, 0.5]) {
    const o = ((phase + k) % 1) - 0.5;
    const cx = px + dx * o * T, cy = py + dy * o * T;
    ctx.beginPath();
    ctx.moveTo(cx - dx * T * 0.1 + nx * half * 0.7, cy - dy * T * 0.1 + ny * half * 0.7);
    ctx.lineTo(cx + dx * T * 0.08, cy + dy * T * 0.08);
    ctx.lineTo(cx - dx * T * 0.1 - nx * half * 0.7, cy - dy * T * 0.1 - ny * half * 0.7);
    ctx.stroke();
  }
  if (b.id === selectedId) { ctx.strokeStyle = FG; ctx.setLineDash([2, 2]); ctx.strokeRect(px - T / 2 + 1, py - T / 2 + 1, T - 2, T - 2); ctx.setLineDash([]); }
}

function drawBeltItems(b, L) {
  const T = L.T;
  const [px, py] = tileCenter(L, b.x, b.y);
  const [dx, dy] = S.DIRS[b.dir];
  ctx.strokeStyle = FG; ctx.lineWidth = 1;
  for (const it of b.items) {
    const o = it.pos - 0.5;
    drawToken(ctx, px + dx * o * T, py + dy * o * T, it.type, Math.max(2.5, T * 0.13));
  }
}

function drawMachine(e, L) {
  const T = L.T, d = S.def(sim, e.type);
  const [px0, py0] = tileCenter(L, e.x, e.y);
  const x = px0 - T / 2, y = py0 - T / 2, w = e.w * T, h = e.h * T;
  const cx = x + w / 2, cy = y + h / 2;
  ctx.save();
  ctx.fillStyle = BG; ctx.strokeStyle = FG; ctx.lineWidth = e.type === 'hub' ? 2 : 1.5;
  if (e.hp < cfg.homeostasis.hp) ctx.setLineDash([4, 2 + (cfg.homeostasis.hp - e.hp) / 12]);
  ctx.fillRect(x + 2, y + 2, w - 4, h - 4);
  ctx.strokeRect(x + 2, y + 2, w - 4, h - 4);
  if (e.type === 'hub') ctx.strokeRect(x + 6, y + 6, w - 12, h - 12);
  ctx.setLineDash([]);

  ctx.fillStyle = FG;
  ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
  const fs = Math.max(9, Math.min(14, T * 0.3));
  ctx.font = `600 ${fs}px "IBM Plex Sans KR", system-ui, sans-serif`;

  if (e.type === 'hub') {
    ctx.fillText(sim.stage === 'prokaryote' ? d.labelProkaryote : d.label, cx, cy - fs * 0.7);
    ctx.font = `${fs}px "IBM Plex Mono", monospace`;
    ctx.fillText(`단백질 ${sim.res.protein}`, cx, cy + fs * 0.7);
  } else if (e.type === 'transporter' || e.type === 'exporter') {
    ctx.lineWidth = 1.5;
    arrow(cx, cy + T * 0.08, e.type === 'transporter' ? e.dir : (e.dir + 2) % 4, T * 0.22);
    ctx.font = `600 ${fs * 0.85}px "IBM Plex Sans KR", system-ui, sans-serif`;
    ctx.fillText(e.type === 'exporter' ? '배출' : e.mode === 'glucose' ? '포' : '아', cx, cy - T * 0.2);
  } else if (e.type === 'splitter') {
    ctx.beginPath(); ctx.moveTo(cx - T * 0.25, cy); ctx.lineTo(cx + T * 0.25, cy); ctx.moveTo(cx, cy - T * 0.25); ctx.lineTo(cx, cy + T * 0.25); ctx.stroke();
    ctx.beginPath(); ctx.arc(cx, cy, T * 0.08, 0, 7); ctx.fill();
  } else if (e.type === 'filter') {
    ctx.lineWidth = 1.5;
    arrow(cx, cy, e.dir, T * 0.3);
    const [fx, fy] = S.DIRS[e.dir];
    ctx.strokeStyle = FG; ctx.lineWidth = 1;
    drawToken(ctx, cx - fx * T * 0.18, cy - fy * T * 0.18, e.mode, Math.max(2.5, T * 0.11));
  } else if (e.type === 'respChain') {
    ctx.beginPath(); ctx.moveTo(cx, cy - T * 0.26); ctx.lineTo(cx + T * 0.24, cy + T * 0.18); ctx.lineTo(cx - T * 0.24, cy + T * 0.18); ctx.closePath(); ctx.stroke();
  } else if (e.type === 'mitochondrion') {
    ctx.beginPath(); ctx.ellipse(cx, cy - h * 0.08, w * 0.34, h * 0.2, 0, 0, 7); ctx.stroke();
    ctx.beginPath();
    for (let k = 0; k <= 6; k++) { const xx = cx - w * 0.24 + (k / 6) * w * 0.48, yy = cy - h * 0.08 + (k % 2 ? 1 : -1) * h * 0.09; k ? ctx.lineTo(xx, yy) : ctx.moveTo(xx, yy); }
    ctx.stroke();
    ctx.fillText(d.label, cx, cy + h * 0.25);
  } else if (e.type === 'golgi') {
    for (let k = 0; k < 4; k++) { ctx.beginPath(); ctx.arc(cx, cy + h * 0.25 + k * h * 0.07, w * 0.3, Math.PI * 1.22, Math.PI * 1.78); ctx.stroke(); }
    ctx.fillText(d.label, cx, cy + h * 0.27);
  } else {
    ctx.fillText(d.short, cx, cy - fs * 0.1);
  }

  if (d.rate) ctx.fillRect(x + 5, y + h - 7, (w - 10) * Math.min(1, e.progress), 2.5);

  // 상태 배지: ● 멈춤, ○ 발효, ⬡ 탈취
  const st = d.rate ? e.status || '' : '';
  const br = Math.max(2.5, Math.min(4.5, T * 0.11)), bx = x + w - br - 3, by = y + br + 3;
  if (e.hijacked) { hex(bx, by, br); }
  else if (/부족|막힘|대기|포화/.test(st) && !/막 정상/.test(st)) {
    ctx.beginPath(); ctx.arc(bx, by, br, 0, 7); ctx.fill();
    if (br >= 4) { ctx.fillStyle = BG; ctx.font = '700 8px system-ui'; ctx.fillText('!', bx, by + 0.5); }
  } else if (/발효/.test(st)) {
    ctx.beginPath(); ctx.arc(bx, by, br, 0, 7); ctx.stroke();
  }
  ctx.restore();
  if (e.id === selectedId) {
    ctx.strokeStyle = FG; ctx.setLineDash([3, 3]); ctx.lineWidth = 1;
    ctx.strokeRect(x - 2, y - 2, w + 4, h + 4); ctx.setLineDash([]);
  }
}

// ---------- 패널 ----------

const fmt = (sec) => `${String(Math.floor(sec / 60)).padStart(2, '0')}:${String(Math.floor(sec % 60)).padStart(2, '0')}`;
const bar = (ratio, mark) => `<div class="bar"><i style="width:${Math.max(0, Math.min(1, ratio)) * 100}%"></i>${mark != null ? `<b style="left:${mark * 100}%"></b>` : ''}</div>`;
const setHTML = (el, html) => { if (el.dataset.k !== html) { el.dataset.k = html; el.innerHTML = html; } };

let panelTick = -1, panelHover = '';
function updatePanel() {
  const hk = (hoverTile ? `${hoverTile.x},${hoverTile.y}` : '') + ':' + selectedId;
  if (sim.tick === panelTick && hk === panelHover && lastHotKey) return;
  panelTick = sim.tick; panelHover = hk;
  const H = cfg.homeostasis;
  $('clock').textContent = fmt(sim.t);
  $('stageLabel').textContent = sim.stage === 'prokaryote' ? `원핵세포 · 반지름 ${S.radius(sim)}` : '진핵세포';

  const parts = [];
  for (const a of sim.threat.active) parts.push(`<strong>${cfg.threats[a.type].label}</strong> 진행 중 · ${Math.ceil(a.until - sim.t)}초 남음`);
  const p = sim.threat.pending;
  if (p && p.warned) parts.push(`경고: <strong>${cfg.threats[p.type].label}</strong> ${Math.ceil(p.at - sim.t)}초 후`);
  if (sim.virus.inside) parts.push(`세포 내 바이러스 ${sim.virus.inside} · 리보솜 탈취 중`);
  setHTML($('threat'), parts.length ? parts.join('<br>') : '위협 없음');
  $('threat').classList.toggle('alarm', parts.length > 0);

  const wr = S.wasteRatio(sim);
  const made = atpHist.length > 1 ? (atpHist.at(-1) - atpHist[0]) / (atpHist.length - 1) : 0;
  const rows = [
    ['ATP', sim.res.atp / cfg.caps.atp, null, `${sim.res.atp}`, sim.crit.atp],
    ['세포질 폐기물', wr, H.wasteCriticalRatio, `${Math.round(wr * 100)}%`, sim.crit.waste],
    ['세포막', sim.membrane / H.membraneMax, H.membraneCritical / H.membraneMax, `${Math.round(sim.membrane)}%`, sim.crit.membrane],
    ['산소 O₂', sim.res.o2 / cfg.caps.o2, null, `${sim.res.o2}`, 0],
    ['단백질 (핵)', sim.res.protein / cfg.caps.protein, null, `${sim.res.protein}`, 0],
  ];
  setHTML($('homeo'), rows.map(([n, r, mk, val, c]) =>
    `<div class="row"><span class="${c > 0 ? 'crit' : ''}">${n}</span>${bar(r, mk)}<span class="val mono">${c > 0 ? `${Math.floor(c)}/${H.criticalSec}s` : val}</span></div>`).join(''));
  setHTML($('econ'), `ATP 생산 ${made.toFixed(1)}/s · 유지비 ${S.totalUpkeep(sim).toFixed(1)}/s${sim.stage === 'prokaryote' ? ` (부피분 ${(cfg.prokaryote.upkeepPerVolume * sim.size ** 3).toFixed(1)})` : ''}`);

  // 핫바
  const types = Object.keys(cfg.buildings).filter((k) => cfg.buildings[k].stages.includes(sim.stage));
  const key = types.map((k) => `${k}${sim.res.protein >= cfg.buildings[k].cost}${S.count(sim, k)}`).join('|') + tool + toolDir + sim.stage + sim.research.done.join();
  if (key !== lastHotKey) {
    lastHotKey = key;
    const box = $('build'); box.innerHTML = '';
    for (const k of types) {
      const d = cfg.buildings[k];
      const b = document.createElement('button');
      b.setAttribute('aria-pressed', tool === k);
      const locked = d.research && !S.researched(sim, d.research);
      b.disabled = (sim.res.protein < d.cost || locked) && tool !== k;
      b.innerHTML = `<span><kbd>${d.key}</kbd> ${d.label}${d.w ? ` <span class="mono">${d.w}×${d.h}</span>` : ''} <span class="mono">×${S.count(sim, k)}</span></span><span class="mono">${d.cost ? `단백질 ${d.cost}` : '무료'}</span><small>${locked ? `잠김: ${cfg.research.nodes[d.research].label} 연구 필요 · ` : ''}${d.desc} · ${d.place === 'membrane' ? '막' : '내부'} · 유지 ${d.upkeep}/s</small>`;
      b.onclick = () => setTool(k);
      box.append(b);
    }
    const rm = document.createElement('button');
    rm.setAttribute('aria-pressed', tool === 'remove');
    rm.innerHTML = `<span><kbd>X</kbd> 제거</span><span class="mono">전액 환불</span><small>우클릭 또는 우클릭 드래그로도 제거</small>`;
    rm.onclick = () => setTool('remove');
    box.append(rm);
    $('dirHint').textContent = tool === 'belt' || tool === 'filter' ? `방향 ${'동남서북'[toolDir]} · R 회전 · 드래그하면 이동 방향을 따름` : '';
  }

  const specs = [];
  const mk = (text, c, fn) => specs.push({ text: text + (c.ok ? '' : ` (${c.why})`), dis: !c.ok || sim.status !== 'running', fn });
  if (sim.stage === 'prokaryote') {
    const g = S.canGrow(sim);
    mk(`성장${g.ok ? ` · 단백질 ${g.cost}` : ''}`, g, () => S.grow(sim));
    const sy = cfg.prokaryote.symbiosis;
    mk(`공생 포획 · 단백질 ${sy.proteinCost}, ATP ${sy.atpCost}`, S.canSymbiosis(sim), () => S.startSymbiosis(sim));
  } else {
    mk(`세포분열 · 단백질 ${cfg.win.proteinForDivision}`, S.canDivide(sim), () => S.divide(sim));
  }
  const ak = specs.map((x) => x.text + x.dis).join('|');
  const act = $('actions');
  if (act.dataset.k !== ak) {
    act.dataset.k = ak; act.innerHTML = '';
    for (const x of specs) { const b = document.createElement('button'); b.textContent = x.text; b.disabled = x.dis; b.onclick = x.fn; act.append(b); }
  }

  // 유전자 발현 연구
  const R = cfg.research.nodes, cur = sim.research.current;
  const rk = Object.keys(R).map((id) => id + S.canResearch(sim, id).ok + S.researched(sim, id)).join('|') + (cur ? cur.id + Math.floor(cur.elapsed) : '');
  const rb = $('research');
  if (rb.dataset.k !== rk) {
    rb.dataset.k = rk; rb.innerHTML = '';
    for (const [id, n] of Object.entries(R)) {
      const c = S.canResearch(sim, id), done = S.researched(sim, id), on = cur && cur.id === id;
      const b = document.createElement('button');
      b.disabled = !c.ok;
      b.setAttribute('aria-pressed', done);
      const tail = done ? '완료' : on ? `${Math.floor(cur.elapsed)}/${n.sec}s` : `단백질 ${n.cost} · ${n.sec}s`;
      b.innerHTML = `<span>${n.label}</span><span class="mono">${tail}</span><small>${n.desc}${!done && !on && !c.ok ? ' · ' + c.why : ''}</small>`;
      if (on) b.style.backgroundImage = `linear-gradient(90deg, rgba(0,0,0,.12) ${(cur.elapsed / cur.duration) * 100}%, transparent 0)`;
      b.onclick = () => { S.startResearch(sim, id); lastHotKey = ''; };
      rb.append(b);
    }
  }

  let e = selectedId && sim.ents.get(selectedId);
  if (!e && selectedId) selectedId = null;
  if (!e && hoverTile && !tool) e = S.entityAt(sim, hoverTile.x, hoverTile.y);
  $('selBox').hidden = !e;
  if (e) inspector(e);

  setHTML($('log'), sim.log.slice(-12).reverse().map((l) => `<div>${fmt(l.t)} ${l.msg}</div>`).join(''));

  if (sim.status !== 'running' && $('overlay').hidden) {
    $('overlay').hidden = false;
    $('ovTitle').textContent = sim.status === 'won' ? '세포분열 완료' : '세포 사멸';
    $('ovReason').textContent = sim.reason;
    $('ovStats').textContent = `${fmt(sim.t)} · 누적 ATP ${sim.stats.atpMade} · 단백질 납품 ${sim.stats.proteinDelivered}`;
  }
}

function inspector(e) {
  const d = S.def(sim, e.type);
  const buf = (o) => Object.entries(o).filter(([, n]) => n > 0).map(([k, n]) => `${ITEM_LABEL[k]} ${n}`).join(', ') || '없음';
  let html = `<div><strong>${e.type === 'hub' && sim.stage === 'prokaryote' ? d.labelProkaryote : d.label}</strong> <span class="mono">(${e.x}, ${e.y})</span></div>`;
  if (e.type === 'belt') {
    html += `<div class="mono small">방향 ${'동남서북'[e.dir]} · 적재 ${e.items.length} · ${e.blocked ? '정체' : '흐름'}</div>`;
  } else {
    html += `<div class="small">${d.desc}</div>`;
    if (e.type !== 'hub') {
      const outs = {};
      for (const it of e.outBuf) outs[it] = (outs[it] || 0) + 1;
      html += `<div class="mono small">상태: ${e.status}${e.hijacked ? ' · 바이러스 탈취' : ''} · 내구 ${Math.round(e.hp)}%</div>`;
      html += `<div class="mono small">입력 ${buf(e.inBuf)} · 출력 ${buf(outs)}</div>`;
    }
  }
  const sel = e.id === selectedId;
  const k = html + sel + e.mode;
  const el = $('sel');
  if (el.dataset.k === k) return;
  el.dataset.k = k;
  el.innerHTML = html + (sel ? '<div class="actions" style="margin-top:6px"></div>' : '');
  if (!sel) return;
  const a = el.querySelector('.actions');
  for (const m of d.modes || []) {
    const b = document.createElement('button');
    b.textContent = ITEM_LABEL[m]; b.setAttribute('aria-pressed', e.mode === m);
    b.onclick = () => S.setMode(sim, e.id, m);
    a.append(b);
  }
  if (e.type === 'belt' || e.type === 'filter') { const b = document.createElement('button'); b.textContent = '회전 (R)'; b.onclick = () => S.rotate(sim, e.x, e.y); a.append(b); }
  if (!d.fixed) {
    const b = document.createElement('button');
    b.textContent = `제거${d.cost ? ` (단백질 +${d.cost})` : ''}`;
    b.onclick = () => { S.remove(sim, e.x, e.y); selectedId = null; };
    a.append(b);
  }
}

boot();
