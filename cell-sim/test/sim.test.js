import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as S from '../src/sim.js';

const cfg = () => JSON.parse(readFileSync(new URL('../config.json', import.meta.url)));
const run = (s, sec) => { for (let i = 0; i < sec * s.cfg.tickHz; i++) S.step(s); };

test('자원은 정수 카운터이고 상한을 넘지 않는다', () => {
  const s = S.createSim(cfg());
  run(s, 50);
  for (const r of S.RES) {
    assert.ok(Number.isInteger(s.res[r]), r);
    assert.ok(s.res[r] >= 0 && s.res[r] <= s.cfg.caps[r], r);
  }
});

test('해당과정만으로 포도당 1 → ATP 2, 호흡 포함 30', () => {
  const c = cfg();
  assert.equal(c.glycolysis.out.atp + c.organelles.mitochondrion.out.atp, 30);
  assert.equal(c.glycolysis.out.atp, 2);
});

test('O₂ 없으면 발효로 폐기물이 쌓인다', () => {
  const c = cfg();
  c.environment.o2Normal = 0; c.start.o2 = 0; c.threats.firstAtSec = 1e9;
  const s = S.createSim(c);
  run(s, 20);
  assert.ok(s.res.waste > 10, `waste=${s.res.waste}`);
});

test('막 슬롯은 면적으로 제한된다', () => {
  const c = cfg(); c.start.protein = 80;
  const s = S.createSim(c);
  assert.equal(S.usedSlots(s), S.membraneSlots(s));
  assert.equal(S.canBuild(s, 'transporter').ok, false);
  assert.ok(S.grow(s));
  assert.ok(S.build(s, 'transporter'));
});

test('공생 → 진핵 → 분열 승리 경로', () => {
  const c = cfg(); c.start.protein = 80; c.start.atp = 300; c.threats.firstAtSec = 1e9;
  const s = S.createSim(c);
  while (S.canGrow(s).ok) S.grow(s);
  s.res.protein = 80; s.res.atp = 300;
  assert.ok(S.startSymbiosis(s));
  run(s, c.prokaryote.symbiosis.integrationSec + 1);
  assert.equal(s.stage, 'eukaryote', s.log.map((l) => l.msg).join('\n'));
  assert.equal(S.count(s, 'nucleus'), 1);
  assert.equal(S.count(s, 'respChain'), 0);
  s.res.protein = 50;
  assert.ok(S.divide(s));
  assert.equal(s.status, 'won');
});

test('ATP 고갈 30초 지속 시 사멸', () => {
  const c = cfg(); c.start.atp = 0; c.start.glucose = 0; c.environment.glucoseSupply = 0; c.threats.firstAtSec = 1e9;
  const s = S.createSim(c);
  run(s, 35);
  assert.equal(s.status, 'lost');
  assert.match(s.reason, /ATP/);
});

test('위협은 15초 전에 예고된다', () => {
  const c = cfg(); c.threats.firstAtSec = 20;
  const s = S.createSim(c);
  run(s, 6);
  assert.ok(s.log.some((l) => l.msg.startsWith('경고')));
  assert.equal(s.threat.active.length, 0);
  run(s, 15);
  assert.equal(s.threat.active.length, 1);
});

test('기본 상태 무조작 3분: 결정적 시드 재현', () => {
  const a = S.createSim(cfg(), { seed: 7 }), b = S.createSim(cfg(), { seed: 7 });
  run(a, 180); run(b, 180);
  assert.deepEqual(a.res, b.res);
  assert.equal(a.status, b.status);
});
