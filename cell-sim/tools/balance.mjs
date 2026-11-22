// 밸런스 점검용 스크립트 봇. 사용: node tools/balance.mjs [-v]
import { readFileSync } from 'node:fs';
import * as S from '../src/sim.js';
const cfg = JSON.parse(readFileSync(new URL('../config.json', import.meta.url)));
const T = (s, mode) => s.org.filter(o => o.type === 'transporter' && o.mode === mode).length;
function buildT(s, mode) { const o = S.build(s, 'transporter'); if (o) S.setMode(s, o.id, mode); return o; }
function play(seed, verbose) {
  const s = S.createSim(cfg, { seed });
  let last = 0, eukAt = null;
  while (s.status === 'running' && s.t < 1200) {
    S.step(s);
    if (s.stage === 'eukaryote' && eukAt === null) eukAt = s.t;
    if (s.tick % 10) continue;
    if (verbose && s.t - last >= 15) { last = s.t; console.log(`t=${s.t.toFixed(0)} ${s.stage[0]} sz=${s.size} mem=${s.membrane.toFixed(0)} up=${S.totalUpkeep(s).toFixed(1)} fer=${s.fermenting} ${JSON.stringify(s.res)} org=${s.org.map(o=>o.type.slice(0,2)+(o.mode?o.mode[0]:'')).join(',')} v=${s.virus.inside} thr=${s.threat.active.map(a=>a.type)}`); }
    const resp = S.count(s, s.stage === 'prokaryote' ? 'respChain' : 'mitochondrion');
    const atpOk = s.res.atp > 60;
    if (s.stage === 'prokaryote') {
      if (s.symbiosis) continue;
      if (S.canSymbiosis(s).ok && resp >= 3) { S.startSymbiosis(s); continue; }
      if (T(s, 'glucose') < resp) { buildT(s, 'glucose'); continue; }
      if (resp < s.size && S.canBuild(s, 'respChain').ok) { S.build(s, 'respChain'); continue; }
      if (S.usedSlots(s) >= S.membraneSlots(s) && S.canGrow(s).ok) { S.grow(s); continue; }
      if (T(s, 'amino') < 3 * S.count(s, 'ribosome')) { buildT(s, 'amino'); continue; }
      if (S.canGrow(s).ok && atpOk) { S.grow(s); continue; }
    } else {
      if (S.canDivide(s).ok) { S.divide(s); break; }
      if (s.res.waste > 25 && S.count(s, 'lysosome') < 2) { S.build(s, 'lysosome'); continue; }
      if (s.membrane < 60 && S.count(s, 'golgi') < 1) { S.build(s, 'golgi'); continue; }
      if (T(s, 'glucose') < resp) { buildT(s, 'glucose'); continue; }
      if (resp < 3) { S.build(s, 'mitochondrion'); continue; }
      if (T(s, 'amino') < 3 * S.count(s, 'ribosome')) { buildT(s, 'amino'); continue; }
      if (S.count(s, 'ribosome') < 2) { S.build(s, 'ribosome'); continue; }
    }
    if (verbose && s.t - last >= 30) { last = s.t; console.log(`t=${s.t.toFixed(0)} ${s.stage[0]} sz=${s.size} mem=${s.membrane.toFixed(0)} up=${S.totalUpkeep(s).toFixed(1)} ${JSON.stringify(s.res)} org=${s.org.map(o=>o.type.slice(0,2)).join(',')} v=${s.virus.inside}`); }
  }
  console.log(`== seed ${seed}: ${s.status} ${s.reason} t=${s.t.toFixed(0)} euk@${eukAt?.toFixed(0)}`);
}
play(1, process.argv.includes('-v'));
for (const sd of [2,3,4,5,6,7,8]) play(sd, false);
