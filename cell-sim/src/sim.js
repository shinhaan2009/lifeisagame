// 세포 관리 시뮬레이션 v0.3 — 팩토리형 시뮬레이션 코어.
// 타일 그리드 위에 소기관(기계)과 미세소관(운반 라인)을 배치한다.
// 아이템(포도당·아미노산·피루브산·단백질·폐기물)은 미세소관 위를 실제로 이동한다.
// ATP·O₂·세포막 내구도·세포질 폐기물은 전역 값이다. DOM 의존 없음, 고정 틱.

export const ITEMS = ['glucose', 'amino', 'pyruvate', 'protein', 'waste'];
export const DIRS = [[1, 0], [0, 1], [-1, 0], [0, -1]]; // 0 동 1 남 2 서 3 북
export const THREATS = ['hypoxia', 'starvation', 'virus'];

function rng(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const key = (x, y) => `${x},${y}`;

// ---------- 생성 ----------

export function createSim(cfg, { seed = 1, starter = true } = {}) {
  const s = {
    cfg,
    rand: rng(seed),
    t: 0,
    tick: 0,
    stage: 'prokaryote',
    size: 1,
    status: 'running',
    reason: '',
    res: { ...cfg.start },
    acc: { o2: 0, upkeep: 0, excrete: 0, synth: 0 },
    membrane: cfg.homeostasis.membraneMax,
    ents: new Map(),
    grid: new Map(),
    nextId: 1,
    env: { o2: cfg.environment.o2Normal, glucose: cfg.environment.glucoseSupply },
    crit: { atp: 0, waste: 0, membrane: 0 },
    threat: { pending: null, active: [] },
    virus: { outside: 0, inside: 0, decay: 0 },
    symbiosis: null,
    research: { done: [], current: null },
    log: [],
    stats: { atpMade: 0, proteinDelivered: 0 },
  };
  s.threat.pending = scheduleThreat(s, cfg.threats.firstAtSec);
  addEntity(s, 'hub', -1, -1, 0);
  if (starter) buildStarter(s);
  log(s, '원핵세포 단계 시작');
  return s;
}

// 시작 생산 라인: 포도당 → 해당 → 막 호흡 / 아미노산 → 리보솜 → 핵양체
function buildStarter(s) {
  addEntity(s, 'transporter', 5, 0, 2, 'glucose');
  addEntity(s, 'belt', 4, 0, 2);
  addEntity(s, 'glyco', 3, 0, 0);
  addEntity(s, 'belt', 3, 1, 1);
  addEntity(s, 'belt', 3, 2, 1);
  addEntity(s, 'belt', 3, 3, 1);
  addEntity(s, 'respChain', 3, 4, 0);
  addEntity(s, 'transporter', -5, 0, 0, 'amino');
  addEntity(s, 'belt', -4, 0, 0);
  addEntity(s, 'ribosome', -3, 0, 0);
  addEntity(s, 'belt', -2, 0, 0);
}

function log(s, msg) {
  s.log.push({ t: s.t, msg });
  if (s.log.length > 60) s.log.shift();
}

export const def = (s, type) => s.cfg.buildings[type];
export const dims = (s, type) => [def(s, type).w || 1, def(s, type).h || 1];

export function radius(s) {
  return s.stage === 'prokaryote' ? s.cfg.prokaryote.radii[s.size - 1] : s.cfg.grid.eukaryoteRadius;
}

export function gridExtent(s) {
  return s.cfg.grid.eukaryoteRadius + s.cfg.grid.margin;
}

export function tileKind(s, x, y) {
  const d = Math.hypot(x, y), r = radius(s);
  if (d < r - 0.5) return 'interior';
  if (d < r + 0.5) return 'membrane';
  return 'env';
}

export function entityAt(s, x, y) {
  const id = s.grid.get(key(x, y));
  return id ? s.ents.get(id) : null;
}

function footprint(e) {
  const out = [];
  for (let i = 0; i < e.w; i++) for (let j = 0; j < e.h; j++) out.push([e.x + i, e.y + j]);
  return out;
}

const inside = (e, x, y) => x >= e.x && x < e.x + e.w && y >= e.y && y < e.y + e.h;

function addEntity(s, type, x, y, dir = 0, mode = null) {
  const [w, h] = dims(s, type);
  const d = def(s, type);
  const e = {
    id: s.nextId++, type, x, y, w, h, dir,
    mode: d.modes ? (mode || d.modes[0]) : null,
    inBuf: {}, outBuf: [], items: type === 'belt' ? [] : null,
    progress: 0, hp: s.cfg.homeostasis.hp, hijacked: false, status: '대기', rr: 0,
  };
  s.ents.set(e.id, e);
  for (const [tx, ty] of footprint(e)) s.grid.set(key(tx, ty), e.id);
  return e;
}

function removeEntity(s, e) {
  for (const [tx, ty] of footprint(e)) s.grid.delete(key(tx, ty));
  s.ents.delete(e.id);
}

const refund = (s, type) => { s.res.protein = Math.min(s.cfg.caps.protein, s.res.protein + def(s, type).cost); };

export const count = (s, type) => [...s.ents.values()].filter((e) => e.type === type).length;

export function totalUpkeep(s) {
  let u = 0;
  const idleF = researched(s, 'conditional') ? s.cfg.research.effects.idleUpkeepFactor : 1;
  for (const e of s.ents.values()) {
    const d = def(s, e.type);
    const base = e.type === 'hub' && s.stage === 'prokaryote' ? d.upkeepProkaryote : d.upkeep;
    u += isIdle(e) ? base * idleF : base;
  }
  if (s.stage === 'prokaryote') u += s.cfg.prokaryote.upkeepPerVolume * s.size ** 3;
  if (s.research.current) u += s.cfg.research.atpPerSec;
  return u;
}

export const isIdle = (e) => e.type !== 'hub' && e.type !== 'belt' && /대기|부족|막힘|포화/.test(e.status || '') && !/작동/.test(e.status || '');

// ---------- 연구 (유전자 발현) ----------

export const researched = (s, id) => s.research.done.includes(id);

export function canResearch(s, id) {
  const n = s.cfg.research.nodes[id];
  if (!n) return { ok: false, why: '없음' };
  if (researched(s, id)) return { ok: false, why: '완료' };
  if (s.research.current) return { ok: false, why: '다른 연구 진행 중' };
  const miss = n.requires.filter((r) => !researched(s, r));
  if (miss.length) return { ok: false, why: `선행: ${miss.map((r) => s.cfg.research.nodes[r].label).join(', ')}` };
  if (s.res.protein < n.cost) return { ok: false, why: `단백질 ${n.cost} 필요` };
  return { ok: true };
}

export function startResearch(s, id) {
  if (!canResearch(s, id).ok || s.status !== 'running') return false;
  const n = s.cfg.research.nodes[id];
  s.res.protein -= n.cost;
  s.research.current = { id, elapsed: 0, duration: n.sec };
  log(s, `유전자 발현 연구 시작: ${n.label}`);
  return true;
}

function updateResearch(s, dt) {
  const c = s.research.current;
  if (!c) return;
  if (s.res.atp <= 0) return; // ATP 없으면 진행 정지
  c.elapsed += dt;
  if (c.elapsed >= c.duration) {
    s.research.done.push(c.id);
    s.research.current = null;
    log(s, `연구 완료: ${s.cfg.research.nodes[c.id].label}`);
  }
}

export const wasteRatio = (s) => s.res.waste / s.cfg.caps.waste;

// ---------- 배치 ----------

export function canPlace(s, type, x, y) {
  const d = def(s, type);
  if (!d || d.fixed) return { ok: false, why: '건설 불가' };
  if (!d.stages.includes(s.stage)) return { ok: false, why: '현재 단계에서 불가' };
  if (d.research && !researched(s, d.research)) return { ok: false, why: `연구 필요: ${s.cfg.research.nodes[d.research].label}` };
  if (s.res.protein < d.cost) return { ok: false, why: `단백질 ${d.cost} 필요` };
  const [w, h] = dims(s, type);
  for (let i = 0; i < w; i++) for (let j = 0; j < h; j++) {
    if (tileKind(s, x + i, y + j) !== d.place) return { ok: false, why: d.place === 'membrane' ? '막 위에만 설치' : '세포 내부에만 설치' };
    if (s.grid.has(key(x + i, y + j))) return { ok: false, why: '이미 점유됨' };
  }
  return { ok: true };
}

export function place(s, type, x, y, dir = 0) {
  if (s.status !== 'running') return null;
  const ex = entityAt(s, x, y);
  if (type === 'belt' && ex && ex.type === 'belt') { ex.dir = dir; return ex; } // 같은 자리: 방향만 변경
  if (!canPlace(s, type, x, y).ok) return null;
  const d = def(s, type);
  s.res.protein -= d.cost;
  if (d.place === 'membrane') dir = inwardDir(x, y);
  return addEntity(s, type, x, y, dir);
}

function inwardDir(x, y) {
  return Math.abs(x) >= Math.abs(y) ? (x > 0 ? 2 : 0) : (y > 0 ? 3 : 1);
}

export function remove(s, x, y) {
  const e = entityAt(s, x, y);
  if (!e || def(s, e.type).fixed) return false;
  removeEntity(s, e);
  refund(s, e.type);
  return true;
}

export function rotate(s, x, y) {
  const e = entityAt(s, x, y);
  if (e && (e.type === 'belt' || e.type === 'filter')) e.dir = (e.dir + 1) % 4;
}

export function setMode(s, id, mode) {
  const e = s.ents.get(id);
  if (e && def(s, e.type).modes?.includes(mode)) e.mode = mode;
}

// ---------- 단계 진행 ----------

export function canGrow(s) {
  const p = s.cfg.prokaryote;
  if (s.stage !== 'prokaryote') return { ok: false, why: '원핵 단계 전용' };
  if (s.size >= p.radii.length) return { ok: false, why: '막 면적 한계' };
  const cost = p.growCost[s.size - 1];
  if (s.res.protein < cost) return { ok: false, why: `단백질 ${cost} 필요` };
  return { ok: true, cost };
}

export function grow(s) {
  const c = canGrow(s);
  if (!c.ok || s.status !== 'running') return false;
  s.res.protein -= c.cost;
  s.size++;
  relocateMembrane(s);
  log(s, `세포 성장 → 반지름 ${radius(s)}. 막 소기관이 새 막으로 이동, 미세소관 재연결 필요`);
  return true;
}

// 막 위 건물을 같은 방위각의 새 막 타일로 옮긴다. 자리가 없거나 단계에 맞지 않으면 환불.
function relocateMembrane(s) {
  const R = gridExtent(s);
  const ring = [];
  for (let x = -R; x <= R; x++) for (let y = -R; y <= R; y++) if (tileKind(s, x, y) === 'membrane') ring.push([x, y]);
  const movers = [...s.ents.values()].filter((e) => def(s, e.type).place === 'membrane');
  for (const e of movers) removeEntity(s, e);
  for (const e of movers) {
    if (!def(s, e.type).stages.includes(s.stage)) { refund(s, e.type); continue; }
    const a = Math.atan2(e.y, e.x);
    let best = null, bd = Infinity;
    for (const [x, y] of ring) {
      if (s.grid.has(key(x, y))) continue;
      const b = Math.atan2(y, x);
      const da = Math.abs(Math.atan2(Math.sin(b - a), Math.cos(b - a)));
      if (da < bd) { bd = da; best = [x, y]; }
    }
    if (!best) { refund(s, e.type); continue; }
    e.x = best[0]; e.y = best[1]; e.dir = inwardDir(e.x, e.y);
    s.ents.set(e.id, e);
    s.grid.set(key(e.x, e.y), e.id);
  }
  // 막 타일과 겹치게 된 내부 건물은 환불 철거
  for (const e of [...s.ents.values()]) {
    if (def(s, e.type).place !== 'interior') continue;
    if (footprint(e).some(([x, y]) => tileKind(s, x, y) !== 'interior')) { removeEntity(s, e); refund(s, e.type); }
  }
}

export function canSymbiosis(s) {
  const p = s.cfg.prokaryote;
  if (s.stage !== 'prokaryote') return { ok: false, why: '원핵 단계 전용' };
  if (s.symbiosis) return { ok: false, why: '진행 중' };
  if (s.size < p.radii.length) return { ok: false, why: '막 면적 한계 미도달' };
  if (s.res.protein < p.symbiosis.proteinCost) return { ok: false, why: `단백질 ${p.symbiosis.proteinCost} 필요` };
  if (s.res.atp < p.symbiosis.atpCost) return { ok: false, why: `ATP ${p.symbiosis.atpCost} 필요` };
  return { ok: true };
}

export function startSymbiosis(s) {
  if (!canSymbiosis(s).ok || s.status !== 'running') return false;
  const sy = s.cfg.prokaryote.symbiosis;
  s.res.protein -= sy.proteinCost;
  s.res.atp -= sy.atpCost;
  s.symbiosis = { elapsed: 0, duration: sy.integrationSec };
  log(s, '호기성 세균 포획: 공생 통합 시작');
  return true;
}

function completeSymbiosis(s) {
  s.stage = 'eukaryote';
  s.symbiosis = null;
  relocateMembrane(s);
  const spot = findFree(s, 'mitochondrion');
  if (spot) addEntity(s, 'mitochondrion', spot[0], spot[1], 0);
  log(s, '공생 성공: 진핵세포 단계. 미토콘드리아 1기 획득, 막 호흡 복합체 환불');
}

function findFree(s, type) {
  const [w, h] = dims(s, type);
  const R = gridExtent(s);
  const cands = [];
  for (let x = -R; x <= R; x++) for (let y = -R; y <= R; y++) cands.push([x, y]);
  cands.sort((a, b) => Math.hypot(a[0], a[1]) - Math.hypot(b[0], b[1]));
  for (const [x, y] of cands) {
    if (x + w >= -2 && x <= 2 && y + h >= -2 && y <= 2) continue; // 핵 둘레 한 칸 비움
    let ok = true;
    for (let i = 0; i < w && ok; i++) for (let j = 0; j < h && ok; j++) {
      if (tileKind(s, x + i, y + j) !== 'interior' || s.grid.has(key(x + i, y + j))) ok = false;
    }
    if (ok) return [x, y];
  }
  return null;
}

export function canDivide(s) {
  const need = s.cfg.win.proteinForDivision;
  if (s.stage !== 'eukaryote') return { ok: false, why: '진핵 단계 필요' };
  if (s.res.protein < need) return { ok: false, why: `핵에 단백질 ${need} 필요` };
  return { ok: true };
}

export function divide(s) {
  if (!canDivide(s).ok || s.status !== 'running') return false;
  s.res.protein -= s.cfg.win.proteinForDivision;
  s.status = 'won';
  s.reason = '세포분열 완료';
  log(s, '세포분열 완료: 승리');
  return true;
}

// ---------- 물류 ----------

function beltHasRoom(s, b) {
  return b.items.length === 0 || b.items[b.items.length - 1].pos >= s.cfg.belt.spacing;
}

export function accepts(s, e, item) {
  const d = def(s, e.type);
  if (e.type === 'hub') return d.accepts.includes(item) && s.res.protein < s.cfg.caps.protein;
  if (e.type === 'splitter' || e.type === 'filter') return e.outBuf.length < 2;
  if (!d.in || !(item in d.in)) return false;
  return (e.inBuf[item] || 0) < Math.max(4, d.in[item] * s.cfg.machineBuffer);
}

function deliver(s, e, item) {
  if (e.type === 'hub') { s.res.protein++; s.stats.proteinDelivered++; return; }
  if (e.type === 'splitter' || e.type === 'filter') { e.outBuf.push(item); return; }
  e.inBuf[item] = (e.inBuf[item] || 0) + 1;
}

// 기계 출력 → 인접한 미세소관 중 기계 쪽을 향하지 않는 것으로 순환 배출
function pushOutputs(s, e) {
  if (!e.outBuf.length) return;
  const cands = [];
  for (let i = -1; i <= e.w; i++) for (let j = -1; j <= e.h; j++) {
    const edgeX = i === -1 || i === e.w, edgeY = j === -1 || j === e.h;
    if (edgeX === edgeY) continue; // 모서리·내부 제외
    const b = entityAt(s, e.x + i, e.y + j);
    if (!b || b.type !== 'belt') continue;
    const [dx, dy] = DIRS[b.dir];
    if (inside(e, b.x + dx, b.y + dy)) continue;
    cands.push(b);
  }
  if (e.type === 'filter') {
    // 지정 아이템은 정면, 나머지는 정면 외 출구로
    const [fx, fy] = DIRS[e.dir];
    const front = cands.filter((b) => b.x === e.x + fx && b.y === e.y + fy);
    const side = cands.filter((b) => !(b.x === e.x + fx && b.y === e.y + fy));
    const item = e.outBuf[0];
    const pool = item === e.mode ? front : side;
    for (let k = 0; k < pool.length; k++) {
      const b = pool[(e.rr + k) % pool.length];
      if (beltHasRoom(s, b)) { b.items.push({ type: e.outBuf.shift(), pos: 0 }); e.rr = (e.rr + k + 1) % Math.max(1, pool.length); break; }
    }
    e.status = e.outBuf.length >= 2 ? '출구 막힘' : '작동';
    return;
  }
  for (let k = 0; k < cands.length && e.outBuf.length; k++) {
    const b = cands[(e.rr + k) % cands.length];
    if (beltHasRoom(s, b)) {
      b.items.push({ type: e.outBuf.shift(), pos: 0 });
      e.rr = (e.rr + k + 1) % cands.length;
    }
  }
}

export const beltSpeed = (s) => s.cfg.belt.speed * (researched(s, 'motors') ? s.cfg.research.effects.motorsSpeed : 1);

function moveBelts(s, dt) {
  const { spacing } = s.cfg.belt;
  const speed = beltSpeed(s);
  for (const b of s.ents.values()) {
    if (b.type !== 'belt') continue;
    if (!b.items.length) { b.blocked = false; continue; }
    for (let i = 0; i < b.items.length; i++) {
      const lim = i === 0 ? 1 : b.items[i - 1].pos - spacing;
      b.items[i].pos = Math.max(b.items[i].pos, Math.min(b.items[i].pos + speed * dt, lim));
    }
    const head = b.items[0];
    if (head.pos < 1) { b.blocked = false; continue; }
    const [dx, dy] = DIRS[b.dir];
    const n = entityAt(s, b.x + dx, b.y + dy);
    let moved = false;
    if (n && n.type === 'belt') {
      if (n.dir !== (b.dir + 2) % 4 && beltHasRoom(s, n)) { n.items.push({ type: head.type, pos: 0 }); moved = true; }
    } else if (n && accepts(s, n, head.type)) {
      deliver(s, n, head.type);
      moved = true;
    }
    if (moved) b.items.shift();
    b.blocked = !moved;
  }
}

// ---------- 기계 ----------

function hasInputs(e, inp) {
  for (const [r, n] of Object.entries(inp || {})) if ((e.inBuf[r] || 0) < n) return false;
  return true;
}

function consume(e, inp) {
  for (const [r, n] of Object.entries(inp || {})) e.inBuf[r] -= n;
}

const addWaste = (s, n) => { s.res.waste = Math.min(s.cfg.caps.waste, s.res.waste + n); };

function craft(s, e) {
  const d = def(s, e.type);
  const cap = s.cfg.caps;

  if (e.type === 'transporter') {
    if (s.res.atp < d.atpIn) { e.status = 'ATP 부족'; return false; }
    if (e.outBuf.length >= s.cfg.outputBuffer) { e.status = '출구 막힘'; return false; }
    s.res.atp -= d.atpIn;
    e.outBuf.push(e.mode);
    e.status = '작동';
    return true;
  }
  if (e.type === 'exporter') {
    if (!(e.inBuf.waste > 0)) { e.status = '입력 대기'; return false; }
    e.inBuf.waste--;
    e.status = '작동';
    return true;
  }
  if (e.type === 'lysosome' && s.virus.inside > 0) {
    s.virus.inside--;
    log(s, '리소좀: 침투 바이러스 분해');
    e.status = '바이러스 분해';
    return true;
  }
  if (e.type === 'golgi' && s.membrane >= d.activeBelowMembrane) { e.status = '막 정상, 대기'; return false; }

  if (!hasInputs(e, d.in)) { e.status = '입력 대기'; return false; }
  if (d.atpIn && s.res.atp < d.atpIn) { e.status = 'ATP 부족'; return false; }
  if (d.o2In && s.res.o2 < d.o2In) { e.status = 'O₂ 부족'; return false; }
  if (d.atpOut && s.res.atp + d.atpOut > cap.atp) { e.status = 'ATP 포화, 억제'; return false; }

  if (e.hijacked) {
    consume(e, d.in);
    if (d.atpIn) s.res.atp -= d.atpIn;
    e.viral = (e.viral || 0) + 1;
    if (e.viral % 3 === 0 && s.virus.inside < 8) { s.virus.inside++; log(s, '바이러스 복제'); }
    e.status = '바이러스 탈취';
    return true;
  }

  const outN = Object.values(d.out || {}).reduce((a, b) => a + b, 0);
  const fits = e.outBuf.length + outN <= s.cfg.outputBuffer;
  if (!fits) {
    if (d.fermentWhenBlocked) {
      consume(e, d.in);
      s.res.atp += d.atpOut || 0;
      s.stats.atpMade += d.atpOut || 0;
      addWaste(s, d.fermentWhenBlocked.waste);
      e.fermented = (e.fermented || 0) + 1;
      e.status = '발효 중 (출구 막힘)';
      return true;
    }
    if (!Object.keys(d.out || {}).every((k) => k === 'waste')) { e.status = '출구 막힘'; return false; }
  }

  consume(e, d.in);
  if (d.atpIn) s.res.atp -= d.atpIn;
  if (d.o2In) s.res.o2 -= d.o2In;
  if (d.atpOut) { s.res.atp += d.atpOut; s.stats.atpMade += d.atpOut; }
  if (d.wasteToPool) addWaste(s, d.wasteToPool);
  if (d.membraneOut) s.membrane = Math.min(s.cfg.homeostasis.membraneMax, s.membrane + d.membraneOut);
  for (const [r, n] of Object.entries(d.out || {})) {
    for (let i = 0; i < n; i++) {
      if (e.outBuf.length < s.cfg.outputBuffer) e.outBuf.push(r);
      else addWaste(s, 1); // 출구 막힌 폐기물은 세포질로 유출
    }
  }
  e.status = fits ? '작동' : '작동, 폐기물 세포질 유출';
  return true;
}

// ---------- 틱 ----------

export function step(s) {
  if (s.status !== 'running') return;
  const cfg = s.cfg, H = cfg.homeostasis;
  const dt = 1 / cfg.tickHz;
  s.t += dt;
  s.tick++;

  updateThreats(s, dt);
  updateResearch(s, dt);

  const wasteCrit = wasteRatio(s) > H.wasteCriticalRatio;
  const eff = (wasteCrit ? H.wasteEfficiency : 1) * (s.res.atp <= 0 ? 0.25 : 1);

  // O₂ 확산
  s.acc.o2 += cfg.environment.o2DiffusionPerSec * s.env.o2 * dt;
  while (s.acc.o2 >= 1) { s.acc.o2--; if (s.res.o2 < cfg.caps.o2) s.res.o2++; }

  // 바이러스 탈취 대상 리보솜
  let h = 0;
  for (const e of s.ents.values()) if (e.type === 'ribosome') e.hijacked = h++ < s.virus.inside;

  // 기계
  for (const e of s.ents.values()) {
    if (e.type === 'belt' || e.type === 'hub') continue;
    if (e.type === 'splitter' || e.type === 'filter') {
      pushOutputs(s, e);
      if (e.type === 'splitter') e.status = e.outBuf.length >= 2 ? '출구 막힘' : '작동';
      continue;
    }
    const d = def(s, e.type);
    let rate = d.rate;
    if (e.type === 'transporter' && e.mode === 'glucose') rate *= s.env.glucose;
    e.progress += rate * eff * dt;
    while (e.progress >= 1) {
      if (!craft(s, e)) { e.progress = 1; break; }
      e.progress -= 1;
    }
    if (d.poolDrainPerSec && s.res.waste > 0 && accepts(s, e, 'waste')) {
      e.drain = (e.drain || 0) + d.poolDrainPerSec * dt;
      if (e.drain >= 1) { e.drain--; s.res.waste--; deliver(s, e, 'waste'); }
    }
    pushOutputs(s, e);
  }

  moveBelts(s, dt);

  // 기본 배출 (막 확산)
  const ex = cfg.excretion;
  s.acc.excrete += (s.stage === 'prokaryote' ? ex.prokaryotePerArea * s.size * s.size : ex.eukaryote) * dt;
  while (s.acc.excrete >= 1) { s.acc.excrete--; if (s.res.waste > 0) s.res.waste--; }

  // 기본 막 합성 (핵 저장 단백질 사용)
  const ms = cfg.membraneSynthesis;
  if (s.membrane < ms.activeBelowMembrane) {
    s.acc.synth += ms.rate * dt;
    if (s.acc.synth >= 1 && s.res.protein >= ms.protein) {
      s.acc.synth--;
      s.res.protein -= ms.protein;
      s.membrane = Math.min(H.membraneMax, s.membrane + ms.membrane);
    }
  }

  // 유지비
  s.acc.upkeep += totalUpkeep(s) * dt;
  while (s.acc.upkeep >= 1) { s.acc.upkeep--; if (s.res.atp > 0) s.res.atp--; }

  // 세포막: 마모 + 바이러스
  const chap = researched(s, 'chaperone');
  s.membrane -= H.membraneWearPerSec * (chap ? cfg.research.effects.chaperoneWear : 1) * dt;
  if (chap && !wasteCrit) {
    for (const e of s.ents.values()) if (e.hp < H.hp) e.hp = Math.min(H.hp, e.hp + cfg.research.effects.chaperoneRegen * dt);
  }
  const V = cfg.threats.virus;
  if (s.virus.outside > 0) {
    s.membrane -= V.membraneDamagePerParticlePerSec * s.virus.outside * dt;
    if (s.membrane < V.infiltrateBelowMembrane && s.rand() < V.infiltrateChancePerSec * s.virus.outside * dt) {
      s.virus.outside--;
      s.virus.inside++;
      log(s, '바이러스 침투: 리보솜 탈취');
    }
  }
  if (s.stage === 'prokaryote' && s.virus.inside > 0 && (s.virus.decay += dt) >= 20) { s.virus.decay = 0; s.virus.inside--; }
  s.membrane = Math.max(0, s.membrane);

  // 폐기물 임계: 소기관 손상
  if (wasteCrit) {
    for (const e of [...s.ents.values()]) {
      if (e.type === 'belt') continue;
      e.hp -= (e.type === 'hub' ? H.nucleusDamagePerSec : H.wasteDamagePerSec) * dt;
      if (e.hp <= 0) {
        if (e.type === 'hub') return lose(s, '핵 파괴');
        removeEntity(s, e);
        log(s, `${def(s, e.type).label} 손상으로 소실`);
      }
    }
  }

  // 막 임계: 내용물 유출
  const memCrit = s.membrane < H.membraneCritical;
  if (memCrit) {
    for (const b of s.ents.values()) {
      if (b.type === 'belt' && b.items.length && s.rand() < H.leakFractionPerSec * 4 * dt) b.items.pop();
    }
    for (const r of ['atp', 'protein']) {
      if (s.rand() < H.leakFractionPerSec * s.res[r] * dt) s.res[r] = Math.max(0, s.res[r] - 1);
    }
  }

  // 항상성 타이머
  s.crit.atp = s.res.atp <= 0 ? s.crit.atp + dt : 0;
  s.crit.waste = wasteCrit ? s.crit.waste + dt : 0;
  s.crit.membrane = memCrit ? s.crit.membrane + dt : 0;
  const names = { atp: 'ATP 고갈', waste: '폐기물 축적', membrane: '세포막 붕괴' };
  for (const k of Object.keys(names)) if (s.crit[k] >= H.criticalSec) return lose(s, `항상성 붕괴: ${names[k]}`);

  // 공생 통합
  if (s.symbiosis) {
    if (s.res.atp <= 0 || wasteCrit || memCrit) {
      s.symbiosis = null;
      log(s, '공생 실패: 항상성 불안정으로 포획 세균 소화됨');
    } else if ((s.symbiosis.elapsed += dt) >= s.symbiosis.duration) completeSymbiosis(s);
  }
}

function lose(s, reason) {
  s.status = 'lost';
  s.reason = reason;
  log(s, `세포 사멸: ${reason}`);
}

function scheduleThreat(s, delay) {
  const pool = s.stage === 'prokaryote' ? ['hypoxia', 'starvation'] : THREATS;
  return { type: pool[Math.floor(s.rand() * pool.length)], at: s.t + delay, warned: false };
}

function updateThreats(s, dt) {
  const T = s.cfg.threats;
  const p = s.threat.pending;
  if (p && !p.warned && s.t >= p.at - T.warningSec) { p.warned = true; log(s, `경고: ${T[p.type].label} ${T.warningSec}초 후 발생`); }
  if (p && s.t >= p.at) {
    const d = T[p.type];
    s.threat.active.push({ type: p.type, until: s.t + d.durationSec });
    if (p.type === 'virus') s.virus.outside += d.particles;
    log(s, `${d.label} 발생`);
    const [a, b] = T.intervalSec;
    s.threat.pending = scheduleThreat(s, d.durationSec + a + s.rand() * (b - a));
  }
  s.threat.active = s.threat.active.filter((a) => {
    if (s.t < a.until) return true;
    if (a.type === 'virus') s.virus.outside = 0;
    log(s, `${T[a.type].label} 종료`);
    return false;
  });
  const on = (type) => s.threat.active.some((a) => a.type === type);
  s.env.o2 = on('hypoxia') ? T.hypoxia.o2Env : s.cfg.environment.o2Normal;
  s.env.glucose = s.cfg.environment.glucoseSupply * (on('starvation') ? T.starvation.glucoseFactor : 1);
}
