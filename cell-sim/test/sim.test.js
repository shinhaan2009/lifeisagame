import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as S from '../src/sim.js';

const cfg = () => JSON.parse(readFileSync(new URL('../config.json', import.meta.url)));
const run = (s, sec) => { for (let i = 0; i < sec * s.cfg.tickHz; i++) S.step(s); };
const calm = (c) => { c.threats.firstAtSec = 1e9; return c; };

test('시작 라인: 단백질이 미세소관을 타고 핵양체에 납품된다', () => {
  const s = S.createSim(calm(cfg()));
  run(s, 60);
  assert.ok(s.stats.proteinDelivered >= 5, `delivered=${s.stats.proteinDelivered}`);
  assert.ok(s.stats.atpMade > 300, `atp=${s.stats.atpMade}`);
  assert.equal(s.status, 'running');
});

test('미세소관 위 아이템은 간격을 유지하고 끝이 막히면 정체된다', () => {
  const c = calm(cfg());
  const s = S.createSim(c, { starter: false });
  s.res.protein = 50;
  S.place(s, 'transporter', 5, 0);
  S.place(s, 'belt', 4, 0, 2);
  S.place(s, 'belt', 3, 0, 2); // 막다른 끝 (2,0)은 빈 타일
  run(s, 30);
  const b1 = S.entityAt(s, 3, 0), b0 = S.entityAt(s, 4, 0);
  assert.equal(b1.items.length, 1 + 1 / c.belt.spacing);
  assert.ok(b1.blocked);
  for (const b of [b0, b1]) for (let i = 1; i < b.items.length; i++) {
    assert.ok(b.items[i - 1].pos - b.items[i].pos >= c.belt.spacing - 1e-9);
  }
  assert.equal(S.entityAt(s, 5, 0).status, '출구 막힘');
});

test('해당 군집 출구가 막히면 발효로 전환되어 세포질 폐기물이 쌓인다', () => {
  const s = S.createSim(calm(cfg()), { starter: false });
  s.res.protein = 50;
  S.place(s, 'transporter', 5, 0);
  S.place(s, 'belt', 4, 0, 2);
  S.place(s, 'glyco', 3, 0);
  run(s, 20);
  assert.ok(S.entityAt(s, 3, 0).fermented > 0);
  assert.ok(s.res.waste > 5, `waste=${s.res.waste}`);
});

test('배치 규칙: 수송체는 막 위, 소기관은 내부만', () => {
  const s = S.createSim(calm(cfg()), { starter: false });
  s.res.protein = 50;
  assert.equal(S.canPlace(s, 'transporter', 2, 2).ok, false);
  assert.equal(S.canPlace(s, 'ribosome', 5, 0).ok, false);
  assert.equal(S.canPlace(s, 'ribosome', 0, 0).ok, false); // 핵양체 점유
  assert.equal(S.canPlace(s, 'mitochondrion', 2, 2).ok, false); // 원핵 단계 불가
  assert.ok(S.place(s, 'ribosome', 3, 0));
});

test('제거 시 단백질 전액 환불', () => {
  const s = S.createSim(calm(cfg()));
  const p = s.res.protein;
  assert.ok(S.remove(s, -3, 0));
  assert.equal(s.res.protein, p + 3);
  assert.equal(S.remove(s, 0, 0), false); // 핵양체 고정
});

test('성장 시 막 소기관이 새 막으로 이동한다', () => {
  const s = S.createSim(calm(cfg()));
  s.res.protein = 100;
  assert.ok(S.grow(s));
  for (const e of s.ents.values()) {
    if (S.def(s, e.type).place === 'membrane') assert.equal(S.tileKind(s, e.x, e.y), 'membrane');
  }
  assert.equal(S.count(s, 'transporter'), 2);
});

test('공생 → 진핵 → 분열 승리 경로', () => {
  const c = calm(cfg()); c.prokaryote.upkeepPerVolume = 0;
  const s = S.createSim(c);
  s.res.protein = 120;
  while (S.canGrow(s).ok) S.grow(s);
  s.res.protein = 120; s.res.atp = 200;
  assert.ok(S.startSymbiosis(s));
  run(s, 21);
  assert.equal(s.stage, 'eukaryote', s.log.map((l) => l.msg).join('\n'));
  assert.equal(S.count(s, 'respChain'), 0);
  assert.equal(S.count(s, 'mitochondrion'), 1);
  s.res.protein = 60;
  assert.ok(S.divide(s));
  assert.equal(s.status, 'won');
});

test('ATP 고갈 30초 지속 시 사멸', () => {
  const c = calm(cfg()); c.start.atp = 0; c.environment.o2Normal = 0; c.start.o2 = 0;
  const s = S.createSim(c);
  run(s, 40);
  assert.equal(s.status, 'lost');
  assert.match(s.reason, /ATP/);
});

test('위협은 15초 전에 예고된다', () => {
  const c = cfg(); c.threats.firstAtSec = 20;
  const s = S.createSim(c);
  run(s, 6);
  assert.ok(s.log.some((l) => l.msg.startsWith('경고')));
  run(s, 15);
  assert.equal(s.threat.active.length, 1);
});

test('결정적 시드 재현', () => {
  const a = S.createSim(cfg(), { seed: 7 }), b = S.createSim(cfg(), { seed: 7 });
  run(a, 180); run(b, 180);
  assert.deepEqual(a.res, b.res);
});

test('연구 전에는 분배기 건설 불가, 연구 후 가능', () => {
  const s = S.createSim(calm(cfg()));
  s.res.protein = 100;
  assert.match(S.canPlace(s, 'splitter', 2, -2).why, /연구/);
  assert.equal(S.canResearch(s, 'filter').ok, false); // 선행 미충족
  assert.ok(S.startResearch(s, 'splitter'));
  assert.equal(S.canResearch(s, 'motors').ok, false); // 진행 중
  run(s, 21);
  assert.ok(S.researched(s, 'splitter'));
  assert.ok(S.canPlace(s, 'splitter', 2, -2).ok);
});

function factory() {
  const c = calm(cfg()); c.prokaryote.upkeepPerVolume = 0;
  const s = S.createSim(c, { starter: false });
  s.res.protein = 100; s.res.atp = 300;
  s.research.done.push('splitter', 'filter');
  return s;
}

test('분배기는 두 출구로 번갈아 보낸다', () => {
  const s = factory();
  S.place(s, 'transporter', 5, 0);
  S.place(s, 'belt', 4, 0, 2);
  S.place(s, 'splitter', 3, 0);
  S.place(s, 'belt', 3, -1, 3);
  S.place(s, 'belt', 3, 1, 1);
  run(s, 6);
  const n = S.entityAt(s, 3, -1).items.length, m = S.entityAt(s, 3, 1).items.length;
  assert.ok(n >= 1 && m >= 1 && Math.abs(n - m) <= 1, `${n} vs ${m}`);
});

test('선별기: 지정 아이템은 정면, 나머지는 옆', () => {
  const s = factory();
  S.place(s, 'transporter', 5, 0);
  const t2 = S.place(s, 'transporter', 3, 4);
  S.setMode(s, t2.id, 'amino');
  S.place(s, 'belt', 4, 0, 2);
  S.place(s, 'belt', 3, 3, 3);
  S.place(s, 'belt', 3, 2, 3);
  S.place(s, 'belt', 3, 1, 3); // (3,0)으로 올라감
  const f = S.place(s, 'filter', 3, 0, 2); // 정면 = 서쪽
  S.setMode(s, f.id, 'amino');
  S.place(s, 'belt', 2, 0, 2);  // 정면 출구
  S.place(s, 'belt', 3, -1, 3); // 옆 출구
  run(s, 12);
  const front = S.entityAt(s, 2, 0).items.map((i) => i.type);
  const side = S.entityAt(s, 3, -1).items.map((i) => i.type);
  assert.ok(front.length && front.every((t) => t === 'amino'), JSON.stringify(front));
  assert.ok(side.length && side.every((t) => t === 'glucose'), JSON.stringify(side));
});

test('조건부 발현: 멈춘 기계 유지비 감소', () => {
  const s = factory();
  S.place(s, 'ribosome', 2, -2);
  S.step(s);
  const before = S.totalUpkeep(s);
  s.research.done.push('conditional');
  assert.ok(S.totalUpkeep(s) < before);
});
