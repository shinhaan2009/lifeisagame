// 배경 공간: 세포가 표류하는 세포외 환경. 시각 전용이며 시뮬레이션 상태를 읽기만 한다.
// 3개 시차 층(원·중·근)의 입자가 세포 이동의 반대 방향으로 흐른다.
// 환경 농도가 입자 밀도에 반영된다: 저산소 → O₂ 감소, 영양 결핍 → 포도당 감소, 바이러스 침입 → 바이러스 군집.

const LAYERS = [
  { p: 0.22, color: '#d4d4d4', density: 1 / 2600, kinds: [['dust', 5], ['o2', 4]] },
  { p: 0.5, color: '#b4b4b4', density: 1 / 9000, kinds: [['glucose', 4], ['amino', 3], ['debris', 2], ['vesicle', 2]] },
  { p: 1.0, color: '#8c8c8c', density: 1 / 60000, kinds: [['rod', 3], ['coccus', 2], ['virus', 3], ['diatom', 1]] },
];

function pick(kinds) {
  const total = kinds.reduce((a, [, w]) => a + w, 0);
  let r = Math.random() * total;
  for (const [k, w] of kinds) if ((r -= w) <= 0) return k;
  return kinds[0][0];
}

export class Space {
  constructor() {
    this.parts = [];
    this.W = 0; this.H = 0;
    this.heading = Math.random() * Math.PI * 2;
    this.turn = 0;
    this.speed = 16; // px/s, 세포 표류 속도
    this.t = 0;
  }

  resize(W, H) {
    if (Math.abs(W - this.W) < 40 && Math.abs(H - this.H) < 40 && this.parts.length) return;
    this.W = W; this.H = H;
    this.parts = [];
    for (const [li, L] of LAYERS.entries()) {
      const n = Math.max(6, Math.round(W * H * L.density));
      for (let i = 0; i < n; i++) this.parts.push(this.spawn(li, Math.random() * W, Math.random() * H));
    }
  }

  spawn(li, x, y) {
    const L = LAYERS[li];
    const kind = pick(L.kinds);
    const big = li === 2;
    return {
      li, kind, x, y,
      u: Math.random(),                 // 농도 문턱값
      rot: Math.random() * Math.PI * 2,
      vr: (Math.random() - 0.5) * (big ? 0.3 : 1.2),
      size: big ? 10 + Math.random() * 10 : li === 1 ? 2.5 + Math.random() * 2 : 0.8 + Math.random() * 0.8,
      swim: big && kind !== 'diatom' ? 4 + Math.random() * 8 : 0, // 자가 운동 (px/s)
      phase: Math.random() * 10,
      jx: 0, jy: 0,
    };
  }

  // 세포 화면 위치의 미세한 흔들림
  bob() {
    return [Math.sin(this.t * 0.37) * 6, Math.cos(this.t * 0.29) * 5];
  }

  update(dt) {
    this.t += dt;
    this.turn += (Math.random() - 0.5) * dt * 0.6;
    this.turn *= 0.995;
    this.heading += this.turn * dt;
    const vx = Math.cos(this.heading) * this.speed, vy = Math.sin(this.heading) * this.speed;
    const m = 60;
    for (const q of this.parts) {
      const L = LAYERS[q.li];
      // 브라운 운동 + 시차 흐름
      q.jx = q.jx * 0.9 + (Math.random() - 0.5) * (q.li === 2 ? 2 : 6);
      q.jy = q.jy * 0.9 + (Math.random() - 0.5) * (q.li === 2 ? 2 : 6);
      q.x += (-vx * L.p + q.jx + Math.cos(q.rot) * q.swim) * dt;
      q.y += (-vy * L.p + q.jy + Math.sin(q.rot) * q.swim) * dt;
      q.rot += q.vr * dt;
      q.phase += dt;
      if (q.x < -m) q.x += this.W + 2 * m; else if (q.x > this.W + m) q.x -= this.W + 2 * m;
      if (q.y < -m) q.y += this.H + 2 * m; else if (q.y > this.H + m) q.y -= this.H + 2 * m;
    }
  }

  visible(q, env) {
    if (q.kind === 'o2') return q.u < env.o2;
    if (q.kind === 'glucose') return q.u < env.glucose;
    if (q.kind === 'virus') return q.u < (env.virus ? 0.95 : 0.12);
    return true;
  }

  draw(g, env) {
    g.save();
    g.lineWidth = 1;
    for (const q of this.parts) {
      if (!this.visible(q, env)) continue;
      const c = LAYERS[q.li].color;
      g.strokeStyle = c; g.fillStyle = c;
      g.save();
      g.translate(q.x, q.y);
      g.rotate(q.rot);
      shape(g, q);
      g.restore();
    }
    g.restore();
  }
}

function shape(g, q) {
  const s = q.size;
  switch (q.kind) {
    case 'dust': g.fillRect(-s / 2, -s / 2, s, s); break;
    case 'o2': g.beginPath(); g.arc(-1.2, 0, 1.3, 0, 7); g.arc(1.2, 0, 1.3, 0, 7); g.stroke(); break;
    case 'glucose': {
      g.beginPath();
      for (let k = 0; k < 6; k++) { const a = (k / 6) * Math.PI * 2; k ? g.lineTo(Math.cos(a) * s, Math.sin(a) * s) : g.moveTo(s, 0); }
      g.closePath(); g.stroke(); break;
    }
    case 'amino': g.beginPath(); g.moveTo(0, -s); g.lineTo(s, s * 0.8); g.lineTo(-s, s * 0.8); g.closePath(); g.stroke(); g.beginPath(); g.moveTo(0, -s); g.lineTo(0, -s * 1.8); g.stroke(); break;
    case 'debris': g.beginPath(); g.moveTo(-s, -s * 0.4); g.lineTo(-s * 0.2, -s); g.lineTo(s, -s * 0.2); g.lineTo(s * 0.4, s); g.lineTo(-s * 0.6, s * 0.5); g.stroke(); break;
    case 'vesicle': g.beginPath(); g.arc(0, 0, s, 0, 7); g.stroke(); g.beginPath(); g.arc(0, 0, s * 0.35, 0, 7); g.fill(); break;
    case 'rod': {
      const L = s * 1.6, r = s * 0.45;
      g.beginPath();
      g.arc(-L / 2 + r, 0, r, Math.PI / 2, Math.PI * 1.5);
      g.lineTo(L / 2 - r, -r);
      g.arc(L / 2 - r, 0, r, -Math.PI / 2, Math.PI / 2);
      g.closePath(); g.stroke();
      // 편모: 진행 반대쪽에서 물결
      g.beginPath(); g.moveTo(-L / 2, 0);
      for (let k = 1; k <= 12; k++) g.lineTo(-L / 2 - k * 1.6, Math.sin(q.phase * 9 + k * 0.9) * 2.2);
      g.stroke();
      break;
    }
    case 'coccus': {
      const r = s * 0.42;
      g.beginPath(); g.arc(-r * 0.95, 0, r, 0, 7); g.stroke();
      g.beginPath(); g.arc(r * 0.95, 0, r, 0, 7); g.stroke();
      break;
    }
    case 'virus': {
      const r = s * 0.35;
      g.beginPath();
      for (let k = 0; k < 6; k++) { const a = (k / 6) * Math.PI * 2; k ? g.lineTo(Math.cos(a) * r, Math.sin(a) * r - r) : g.moveTo(r, -r); }
      g.closePath(); g.stroke();
      g.beginPath(); g.moveTo(0, 0); g.lineTo(0, r * 1.6);
      g.moveTo(0, r * 1.6); g.lineTo(-r, r * 2.4); g.moveTo(0, r * 1.6); g.lineTo(r, r * 2.4);
      g.stroke();
      break;
    }
    case 'diatom': {
      g.beginPath(); g.ellipse(0, 0, s * 1.3, s * 0.5, 0, 0, 7); g.stroke();
      for (let k = -3; k <= 3; k++) { g.beginPath(); g.moveTo(k * s * 0.3, -s * 0.45); g.lineTo(k * s * 0.3, s * 0.45); g.stroke(); }
      break;
    }
  }
}
