// 연구: 5개 축, 일일 예산 배분, 밤마다 확률적 결과(실패·부분 성공·돌파), AI 연구 보조의 추천.
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabMeat.Sim
{
    public partial class Game
    {
        public Dictionary<string, AxisState> axes = new Dictionary<string, AxisState>();
        public int computeTier;
        public string aiAdvice = "";
        public string aiAdviceAxis = "";

        void InitResearch()
        {
            var M = B.researchModel;
            foreach (var a in B.research)
            {
                axes[a.id] = new AxisState { id = a.id, hiddenMult = (float)Math.Exp(rng.Range((float)Math.Log(M.hiddenMultMin), (float)Math.Log(M.hiddenMultMax))) };
            }
            UpdateAdvice();
        }

        public int Level(string axis) { AxisState a; return axes.TryGetValue(axis, out a) ? a.level : 0; }

        public float Difficulty(string axis)
        {
            var d = ADef(axis); var a = axes[axis];
            return d.baseDifficulty * (float)Math.Pow(d.difficultyGrowth, a.level) * a.hiddenMult;
        }

        public float ResearchEfficiency(string axis)
        {
            float m = B.compute[computeTier].researchMult;
            foreach (var b in buffs) if (b.kind == "research" && b.axis == axis && day <= b.untilDay) m *= b.mult;
            return m;
        }

        public float DailyPoints(string axis)
        {
            var a = axes[axis];
            return a.budget * ResearchEfficiency(axis) + StaffResearchPoints(axis);
        }

        public float SuccessChance(string axis, float extraPoints = 0)
        {
            var a = axes[axis];
            if (a.level >= ADef(axis).maxLevel) return 0;
            float pts = a.points + extraPoints;
            if (pts <= 0) return 0;
            return 1f - (float)Math.Exp(-pts / Difficulty(axis));
        }

        public float TotalResearchBudget { get { return axes.Values.Sum(a => a.budget); } }

        public void SetBudget(string axis, float perDay) { axes[axis].budget = Math.Max(0, perDay); }

        void ResolveResearch()
        {
            var M = B.researchModel;
            foreach (var a in axes.Values)
            {
                var def = ADef(a.id);
                float spend = a.budget;
                if (spend > 0) Spend("연구", spend);
                float gained = DailyPoints(a.id);
                if (gained <= 0) { a.lastOutcome = Outcome.None; a.lastNote = ""; continue; }
                if (a.level >= def.maxLevel) { a.lastOutcome = Outcome.None; a.lastNote = "최고 수준 도달"; continue; }
                a.points += gained;
                float p = SuccessChance(a.id);
                if (rng.Chance(p))
                {
                    bool big = rng.Chance(M.breakthroughShare);
                    a.progress += big ? 1f + M.partialProgress : M.partialProgress;
                    a.lastOutcome = big ? Outcome.Breakthrough : Outcome.Partial;
                    a.points = 0;
                    int before = a.level;
                    while (a.progress >= 1f && a.level < def.maxLevel) { a.progress -= 1f; a.level++; }
                    if (a.level >= def.maxLevel) a.progress = 0;
                    a.lastNote = big ? "돌파" : "부분 성공";
                    if (a.level > before) a.lastNote += $" → Lv{a.level}";
                    ledger.notes.Add($"연구 {def.label}: {a.lastNote} (성공 확률 {p:P0})");
                    if (a.level > before) Notify("AI", $"{def.label} Lv{a.level} 달성. {def.effect}");
                }
                else
                {
                    a.points *= M.failKeep;
                    a.lastOutcome = Outcome.Fail;
                    a.lastNote = "실패";
                    ledger.notes.Add($"연구 {def.label}: 실패 (성공 확률 {p:P0}, 누적 투입 일부 유지)");
                }
            }
            UpdateAdvice();
        }

        // AI는 숨은 난이도를 정확도만큼만 알고 추정한다. 틀릴 수 있다.
        public void UpdateAdvice()
        {
            float acc = B.compute[computeTier].aiAccuracy;
            string best = null; float bestScore = -1;
            var weights = new Dictionary<string, float> { { "serumFree", 1.4f }, { "cellLine", 1.0f }, { "sterility", 0.9f }, { "scaffold", 0.5f }, { "vascular", 0.4f } };
            foreach (var a in axes.Values)
            {
                var def = ADef(a.id);
                if (a.level >= def.maxLevel) { a.aiEstimate = 0; continue; }
                float noise = rng.Normal(0, (1f - acc) * 0.8f);
                float believedMult = a.hiddenMult * (float)Math.Exp(noise);
                float diff = def.baseDifficulty * (float)Math.Pow(def.difficultyGrowth, a.level) * believedMult;
                float pts = a.points + Math.Max(a.budget, 200f) * ResearchEfficiency(a.id) + StaffResearchPoints(a.id);
                a.aiEstimate = 1f - (float)Math.Exp(-pts / diff);
                float w; weights.TryGetValue(a.id, out w);
                float score = a.aiEstimate * w / (float)Math.Pow(def.difficultyGrowth, a.level * 0.5f);
                if (score > bestScore) { bestScore = score; best = a.id; }
            }
            aiAdviceAxis = best ?? "";
            if (best != null)
            {
                var d = ADef(best);
                aiAdvice = $"{d.label}에 예산을 집중하길 권합니다. 내일 성공 확률 추정 {axes[best].aiEstimate:P0} (모델 신뢰도 {acc:P0}). 근거: {d.theory}";
            }
        }

        public string BuyCompute(int tier)
        {
            if (tier <= computeTier || tier >= B.compute.Count) return "업그레이드할 수 없다";
            var c = B.compute[tier];
            if (phase < c.phase) return $"Phase {c.phase}부터";
            if (!CanAfford(c.price)) return "현금 부족";
            Spend("설비", c.price);
            computeTier = tier;
            UpdateAdvice();
            return null;
        }
    }
}
