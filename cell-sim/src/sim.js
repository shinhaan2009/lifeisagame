// 세포 관리 시뮬레이션 v0.1 — 시뮬레이션 코어.
// DOM 의존 없음. 고정 틱(cfg.tickHz)으로 step()을 호출한다. 자원은 정수 카운터.

export const RES = ['glucose', 'o2', 'amino', 'atp', 'pyruvate', 'protein', 'waste'];
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

export function createSim(cfg, { seed = 1 } = {}) {
  const s = {
    cfg,
    rand: rng(seed),
    t: 0,
    tick: 0,
    stage: 'prokaryote',
    status: 'running',
    reason: '',
    res: { ...cfg.start },
    acc: { o2: 0, upkeep: 0, excrete: 0, wear: 0, leak: {} },
    membrane: cfg.homeostasis.membraneMax,
    size: cfg.prokaryote.startSize,
    org: [],
    nextId: 1,
    env: { o2: cfg.environment.o2Normal, glucose: cfg.environment.glucoseSupply },
    crit: { atp: 0, waste: 0, membrane: 0 },
    threat: { pending: null, active: [] },
    virus: { outside: 0, inside: 0, decay: 0 },
    symbiosis: null,
    flows: [],
    log: [],
    stats: { atpMade: 0, proteinMade: 0 },
  };
  s.threat.pending = scheduleThreat(s, cfg.threats.firstAtSec);
  for (const [type, n] of Object.entries(cfg.prokaryote.startOrganelles)) {
    for (let i = 0; i < n; i++) addOrganelle(s, type);
  }
  log(s, '원핵세포 단계 시작');
  return s;
}

function log(s, msg) {
  s.log.push({ t: s.t, msg });
  if (s.log.length > 50) s.log.shift();
}

function scheduleThreat(s, delay) {
  const pool = s.stage === 'prokaryote' ? ['hypoxia', 'starvation'] : THREATS;
  const type = pool[Math.floor(s.rand() * pool.length)];
  return { type, at: s.t + delay, warned: false };
}

function addOrganelle(s, type, pos) {
  const o = {
    id: s.nextId++,
    type,
    hp: s.cfg.homeostasis.organelleHp,
    progress: 0,
    mode: type === 'transporter' ? (s.org.filter((x) => x.type === 'transporter').length % 2 ? 'amino' : 'glucose') : null,
    hijacked: false,
    x: pos?.x ?? null,
    y: pos?.y ?? null,
  };
  s.org.push(o);
  return o;
}

export const count = (s, type) => s.org.filter((o) => o.type === type).length;

export function membraneSlots(s) {
  if (s.stage !== 'prokaryote') return Infinity;
  return s.cfg.prokaryote.slotsPerArea * s.size * s.size;
}

export function usedSlots(s) {
  return s.org.filter((o) => s.cfg.organelles[o.type].membraneSlot).length;
}

export function totalUpkeep(s) {
  let u = 0;
  for (const o of s.org) u += s.cfg.organelles[o.type].upkeep;
  if (s.stage === 'prokaryote') u += s.cfg.prokaryote.upkeepPerVolume * s.size ** 3;
  return u;
}

// ---------- 플레이어 액션 ----------

export function canBuild(s, type) {
  const def = s.cfg.organelles[type];
  if (!def || def.fixed) return { ok: false, why: '건설 불가' };
  if (!def.stages.includes(s.stage)) return { ok: false, why: '현재 단계에서 불가' };
  if (s.res.protein < def.cost) return { ok: false, why: `단백질 ${def.cost} 필요` };
  if (def.membraneSlot && usedSlots(s) >= membraneSlots(s)) return { ok: false, why: '막 면적 부족' };
  return { ok: true };
}

export function build(s, type, pos) {
  if (s.status !== 'running') return null;
  const c = canBuild(s, type);
  if (!c.ok) return null;
  s.res.protein -= s.cfg.organelles[type].cost;
  const o = addOrganelle(s, type, pos);
  log(s, `${s.cfg.organelles[type].label} 건설`);
  return o;
}

export function demolish(s, id) {
  const i = s.org.findIndex((o) => o.id === id);
  if (i < 0) return false;
  const o = s.org[i];
  const def = s.cfg.organelles[o.type];
  if (def.fixed) return false;
  s.org.splice(i, 1);
  s.res.protein = Math.min(s.cfg.caps.protein, s.res.protein + Math.floor(def.cost / 2));
  if (o.hijacked) s.virus.inside = Math.max(0, s.virus.inside - 1);
  log(s, `${def.label} 해체`);
  return true;
}

export function setMode(s, id, mode) {
  const o = s.org.find((x) => x.id === id);
  if (o && o.type === 'transporter' && (mode === 'glucose' || mode === 'amino')) o.mode = mode;
}

export function canGrow(s) {
  const p = s.cfg.prokaryote;
  if (s.stage !== 'prokaryote') return { ok: false, why: '원핵 단계 전용' };
  if (s.size >= p.maxSize) return { ok: false, why: '막 면적 한계' };
  const cost = p.growCostPerSize * s.size;
  if (s.res.protein < cost) return { ok: false, why: `단백질 ${cost} 필요` };
  return { ok: true, cost };
}

export function grow(s) {
  const c = canGrow(s);
  if (!c.ok || s.status !== 'running') return false;
  s.res.protein -= c.cost;
  s.size += 1;
  log(s, `세포 성장 → 크기 ${s.size}`);
  return true;
}

export function canSymbiosis(s) {
  const p = s.cfg.prokaryote;
  if (s.stage !== 'prokaryote') return { ok: false, why: '원핵 단계 전용' };
  if (s.symbiosis) return { ok: false, why: '진행 중' };
  if (s.size < p.maxSize) return { ok: false, why: '막 면적 한계 미도달' };
  if (s.res.protein < p.symbiosis.proteinCost) return { ok: false, why: `단백질 ${p.symbiosis.proteinCost} 필요` };
  if (s.res.atp < p.symbiosis.atpCost) return { ok: false, why: `ATP ${p.symbiosis.atpCost} 필요` };
  return { ok: true };
}

export function startSymbiosis(s) {
  const c = canSymbiosis(s);
  if (!c.ok || s.status !== 'running') return false;
  const sy = s.cfg.prokaryote.symbiosis;
  s.res.protein -= sy.proteinCost;
  s.res.atp -= sy.atpCost;
  s.symbiosis = { elapsed: 0, duration: sy.integrationSec };
  log(s, '호기성 세균 포획 — 공생 통합 시작');
  return true;
}

function completeSymbiosis(s) {
  const e = s.cfg.eukaryote;
  s.stage = 'eukaryote';
  s.symbiosis = null;
  s.org = s.org.filter((o) => !e.removeOnSymbiosis.includes(o.type));
  for (const [type, n] of Object.entries(e.grantOnSymbiosis)) {
    for (let i = 0; i < n; i++) addOrganelle(s, type);
  }
  log(s, '공생 성공 — 진핵세포 단계 진입');
}

export function canDivide(s) {
  const need = s.cfg.win.proteinForDivision;
  if (s.stage !== 'eukaryote') return { ok: false, why: '진핵 단계 필요' };
  if (s.res.protein < need) return { ok: false, why: `단백질 ${need} 필요` };
  return { ok: true };
}

export function divide(s) {
  if (!canDivide(s).ok || s.status !== 'running') return false;
  s.res.protein -= s.cfg.win.proteinForDivision;
  s.status = 'won';
  s.reason = '세포분열 완료';
  log(s, '세포분열 완료 — 승리');
  return true;
}

// ---------- 틱 ----------

function cap(s, r) {
  return s.cfg.caps[r];
}

function hasInputs(s, inp) {
  for (const [r, n] of Object.entries(inp)) if (s.res[r] < n) return false;
  return true;
}

function hasRoom(s, out) {
  for (const [r, n] of Object.entries(out)) {
    if (r.startsWith('$')) continue;
    if (r === 'waste') continue; // 폐기물은 상한 초과 허용 대신 상한에서 잘림 — 생산 자체는 막지 않는다
    if (s.res[r] + n > cap(s, r)) return false;
  }
  return true;
}

function apply(s, inp, out) {
  for (const [r, n] of Object.entries(inp)) s.res[r] -= n;
  for (const [r, n] of Object.entries(out)) {
    if (r.startsWith('$')) continue;
    s.res[r] = Math.min(cap(s, r), s.res[r] + n);
  }
}

function flow(s, src, dst, res) {
  if (s.flows.length < 200) s.flows.push({ src, dst, res });
}

// 반응기 하나를 진행. 반응 1회 성공할 때마다 onFire 호출.
function runReactor(s, holder, rate, dt, tryFire) {
  holder.progress += rate * dt;
  let fired = 0;
  while (holder.progress >= 1) {
    if (!tryFire()) {
      holder.progress = 1; // 막힘: 진행도 유지, 입력 대기
      break;
    }
    holder.progress -= 1;
    fired++;
  }
  return fired;
}

export function wasteRatio(s) {
  return s.res.waste / cap(s, 'waste');
}

export function step(s) {
  if (s.status !== 'running') return;
  const cfg = s.cfg;
  const H = cfg.homeostasis;
  const dt = 1 / cfg.tickHz;
  s.t += dt;
  s.tick++;
  s.flows = [];

  updateThreats(s, dt);

  const wasteCrit = wasteRatio(s) > H.wasteCriticalRatio;
  const eff = wasteCrit ? H.wasteEfficiency : 1;

  // 1. 산소 확산 (환경 농도 비례)
  s.acc.o2 += cfg.environment.o2DiffusionPerSec * s.env.o2 * dt;
  while (s.acc.o2 >= 1) {
    s.acc.o2 -= 1;
    if (s.res.o2 < cap(s, 'o2')) { s.res.o2++; flow(s, 'env', 'cyto', 'o2'); }
  }

  // 1b. 포도당 수동 확산 (소량, ATP 고갈 시 회복 여지)
  s.acc.glc = (s.acc.glc || 0) + cfg.environment.glucosePassivePerSec * s.env.glucose * dt;
  while (s.acc.glc >= 1) {
    s.acc.glc -= 1;
    if (s.res.glucose < cap(s, 'glucose')) s.res.glucose++;
  }

  // 2. 수송체
  for (const o of s.org) {
    if (o.type !== 'transporter') continue;
    const def = cfg.organelles.transporter;
    const r = o.mode === 'glucose' ? def.rate * s.env.glucose : def.rate;
    runReactor(s, o, r * eff, dt, () => {
      if (s.res.atp < 1 || s.res[o.mode] >= cap(s, o.mode)) return false;
      s.res.atp -= 1;
      s.res[o.mode] += 1;
      flow(s, 'env', o.id, o.mode);
      return true;
    });
  }

  // 3. 해당과정 (세포 기본 기능)
  const gly = cfg.glycolysis;
  s.glyHolder ??= { progress: 0 };
  runReactor(s, s.glyHolder, gly.rate * eff, dt, () => {
    if (!hasInputs(s, gly.in) || !hasRoom(s, gly.out)) return false;
    apply(s, gly.in, gly.out);
    s.stats.atpMade += gly.out.atp;
    flow(s, 'cyto', 'cyto', 'pyruvate');
    return true;
  });

  // 4. 호기성 호흡 (막 호흡 복합체 / 미토콘드리아)
  for (const o of s.org) {
    if (o.type !== 'respChain' && o.type !== 'mitochondrion') continue;
    const def = cfg.organelles[o.type];
    runReactor(s, o, def.rate * eff, dt, () => {
      // ATP 포화 시 호흡 억제 (피드백 저해)
      if (!hasInputs(s, def.in) || !hasRoom(s, def.out)) return false;
      apply(s, def.in, def.out);
      s.stats.atpMade += def.out.atp;
      flow(s, o.id, 'cyto', 'atp');
      return true;
    });
  }

  // 5. 발효 (자동): O₂ 부족 또는 피루브산 적체(호흡 포화)
  const fer = cfg.fermentation;
  s.ferHolder ??= { progress: 0 };
  s.fermenting = s.res.pyruvate >= 2 && (s.res.o2 < 1 || s.res.pyruvate >= cap(s, 'pyruvate') / 2);
  if (s.fermenting) {
    runReactor(s, s.ferHolder, fer.rate, dt, () => {
      if (s.res.pyruvate < 2 || !(s.res.o2 < 1 || s.res.pyruvate >= cap(s, 'pyruvate') / 2)) return false;
      apply(s, fer.in, fer.out);
      return true;
    });
  } else {
    s.ferHolder.progress = 0;
  }

  // 6. 리보솜 (탈취된 리보솜은 바이러스를 복제)
  const hijackTarget = Math.min(s.virus.inside, count(s, 'ribosome'));
  let h = 0;
  for (const o of s.org) if (o.type === 'ribosome') o.hijacked = h++ < hijackTarget;
  for (const o of s.org) {
    if (o.type !== 'ribosome') continue;
    const def = cfg.organelles.ribosome;
    runReactor(s, o, def.rate * eff, dt, () => {
      if (!hasInputs(s, def.in)) return false;
      if (o.hijacked) {
        for (const [r, n] of Object.entries(def.in)) s.res[r] -= n;
        o.viralCycles = (o.viralCycles || 0) + 1;
        if (o.viralCycles % 3 === 0 && s.virus.inside < 8) { s.virus.inside++; log(s, '바이러스 복제'); }
        return true;
      }
      if (!hasRoom(s, def.out)) return false;
      apply(s, def.in, def.out);
      s.stats.proteinMade++;
      flow(s, o.id, 'cyto', 'protein');
      return true;
    });
  }

  // 7. 리소좀: 침투 바이러스 우선 분해, 아니면 폐기물 → 아미노산
  for (const o of s.org) {
    if (o.type !== 'lysosome') continue;
    const def = cfg.organelles.lysosome;
    runReactor(s, o, def.rate * eff, dt, () => {
      if (s.virus.inside > 0) { s.virus.inside--; log(s, '리소좀: 바이러스 분해'); return true; }
      if (!hasInputs(s, def.in) || !hasRoom(s, def.out)) return false;
      apply(s, def.in, def.out);
      flow(s, 'cyto', o.id, 'waste');
      return true;
    });
  }

  // 8. 골지체: 막 수리
  for (const o of s.org) {
    if (o.type !== 'golgi') continue;
    const def = cfg.organelles.golgi;
    if (s.membrane >= def.activeBelowMembrane) { o.progress = 0; continue; }
    runReactor(s, o, def.rate * eff, dt, () => {
      if (!hasInputs(s, def.in)) return false;
      apply(s, def.in, {});
      s.membrane = Math.min(H.membraneMax, s.membrane + def.out.$membrane);
      flow(s, o.id, 'membrane', 'protein');
      return true;
    });
  }

  // 8b. 기본 막 합성 (모든 단계, 자연 마모 상쇄 수준)
  const ms = cfg.membraneSynthesis;
  s.msHolder ??= { progress: 0 };
  if (s.membrane < ms.activeBelowMembrane) {
    runReactor(s, s.msHolder, ms.rate * eff, dt, () => {
      if (!hasInputs(s, ms.in)) return false;
      apply(s, ms.in, {});
      s.membrane = Math.min(H.membraneMax, s.membrane + ms.membrane);
      return true;
    });
  } else s.msHolder.progress = 0;

  // 9. 기본 배출
  const ex = cfg.baseExcretionPerSec;
  const excr = s.stage === 'prokaryote' ? ex.prokaryotePerArea * s.size * s.size : ex.eukaryote;
  s.acc.excrete += excr * dt;
  while (s.acc.excrete >= 1) {
    s.acc.excrete -= 1;
    if (s.res.waste > 0) { s.res.waste--; flow(s, 'cyto', 'env', 'waste'); }
  }

  // 10. 유지비
  s.acc.upkeep += totalUpkeep(s) * dt;
  while (s.acc.upkeep >= 1) {
    s.acc.upkeep -= 1;
    if (s.res.atp > 0) s.res.atp--;
  }

  // 11. 세포막: 마모 + 바이러스
  s.membrane -= H.membraneWearPerSec * dt;
  const vcfg = cfg.threats.virus;
  if (s.virus.outside > 0) {
    s.membrane -= vcfg.membraneDamagePerParticlePerSec * s.virus.outside * dt;
    if (s.membrane < vcfg.infiltrateBelowMembrane) {
      for (let i = 0; i < s.virus.outside; i++) {
        if (s.rand() < vcfg.infiltrateChancePerSec * dt) {
          s.virus.outside--; s.virus.inside++;
          log(s, '바이러스 침투 — 리보솜 탈취');
          break;
        }
      }
    }
  }
  if (s.stage === 'prokaryote' && s.virus.inside > 0) {
    s.virus.decay += dt;
    if (s.virus.decay >= 20) { s.virus.decay = 0; s.virus.inside--; }
  }
  s.membrane = Math.max(0, s.membrane);

  // 12. 폐기물 임계: 소기관 손상
  if (wasteCrit) {
    for (const o of s.org) {
      o.hp -= (o.type === 'nucleus' ? H.nucleusDamagePerSec : H.wasteDamagePerSec) * dt;
    }
    const dead = s.org.filter((o) => o.hp <= 0);
    for (const o of dead) {
      if (o.type === 'nucleus') return lose(s, '핵 파괴');
      s.org.splice(s.org.indexOf(o), 1);
      log(s, `${cfg.organelles[o.type].label} 손상으로 소실`);
    }
  }

  // 13. 막 임계: 내용물 유출
  const memCrit = s.membrane < H.membraneCritical;
  if (memCrit) {
    for (const r of RES) {
      s.acc.leak[r] = (s.acc.leak[r] || 0) + s.res[r] * H.leakFractionPerSec * dt;
      const n = Math.floor(s.acc.leak[r]);
      if (n > 0) { s.res[r] = Math.max(0, s.res[r] - n); s.acc.leak[r] -= n; }
    }
  }

  // 14. 항상성 타이머
  const atpCrit = s.res.atp <= 0;
  s.crit.atp = atpCrit ? s.crit.atp + dt : 0;
  s.crit.waste = wasteCrit ? s.crit.waste + dt : 0;
  s.crit.membrane = memCrit ? s.crit.membrane + dt : 0;
  for (const k of ['atp', 'waste', 'membrane']) {
    if (s.crit[k] >= H.criticalSec) {
      const names = { atp: 'ATP 고갈', waste: '폐기물 축적', membrane: '세포막 붕괴' };
      return lose(s, `항상성 붕괴: ${names[k]}`);
    }
  }

  // 15. 공생 통합
  if (s.symbiosis) {
    if (atpCrit || wasteCrit || memCrit) {
      s.symbiosis = null;
      log(s, '공생 실패 — 항상성 불안정으로 포획 세균 소화됨');
    } else {
      s.symbiosis.elapsed += dt;
      if (s.symbiosis.elapsed >= s.symbiosis.duration) completeSymbiosis(s);
    }
  }
}

function lose(s, reason) {
  s.status = 'lost';
  s.reason = reason;
  log(s, `세포 사멸 — ${reason}`);
}

function updateThreats(s, dt) {
  const T = s.cfg.threats;
  const p = s.threat.pending;
  if (p && !p.warned && s.t >= p.at - T.warningSec) {
    p.warned = true;
    log(s, `경고: ${T[p.type].label} ${T.warningSec}초 후 발생`);
  }
  if (p && s.t >= p.at) {
    const def = T[p.type];
    s.threat.active.push({ type: p.type, until: s.t + def.durationSec });
    if (p.type === 'virus') s.virus.outside += def.particles;
    log(s, `${def.label} 발생`);
    const [a, b] = T.intervalSec;
    s.threat.pending = scheduleThreat(s, def.durationSec + a + s.rand() * (b - a));
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
