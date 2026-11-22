// 시설 패널: 리액터, 작업대, 냉장고, 배달, 레스토랑, 택시, 면접, 정산, 사건, 메뉴.
using System.Linq;
using UnityEngine;
using LabMeat.Sim;

namespace LabMeat.Game
{
    public partial class GameManager
    {
        int interviewId;
        string speciesPick = "chicken";
        string lastReply = "";

        // ---------- 리액터 ----------

        void ReactorPanel()
        {
            var r = sim.ReactorAt(target.location, target.index);
            if (r == null) { panel = Panel.None; return; }
            var d = sim.RDef(r.def);
            Begin($"{sim.LDef(r.location).label} 리액터 {r.slot + 1} · {d.label}", 640, 520);
            Ui.L($"상태: {StateText(r)}");
            if (r.state == ReactorState.Growing || r.state == ReactorState.Ready)
            {
                GUILayout.BeginHorizontal(); Ui.S("배양 진행"); Ui.Bar(r.growth, 220); GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal(); Ui.S("배지 신선도"); Ui.Bar(r.freshness / 100f, 220); GUILayout.EndHorizontal();
                Ui.S($"배치 누적 비용 {Ui.Money(r.batchCost)} · 시작 {sim.Fmt(r.startMinute)}" + (r.state == ReactorState.Ready && r.overripeHours > 24 ? " · 수확이 늦어 수확량이 줄고 있다" : ""));
            }
            if (!string.IsNullOrEmpty(r.lastCheck)) Ui.L("최근 점검: " + r.lastCheck);
            Ui.S($"차고 재고: 세포 바이알 {sim.stock["vial"]} · 배지 {sim.stock["medium"]}L · 1회 사용량 {d.capacityL}L · 배지 단가 {Ui.Money(sim.MediumUnitPrice)}/L");
            GUILayout.Space(8);
            switch (r.state)
            {
                case ReactorState.Empty:
                    Ui.H("세포 해동·접종");
                    GUILayout.BeginHorizontal();
                    foreach (var s in B.species)
                    {
                        bool ok = sim.SpeciesUnlocked(s.id);
                        string text = ok ? s.label : $"{s.label} (세포주 Lv{s.cellLineRequired})";
                        if (GUILayout.Button(text, speciesPick == s.id ? Ui.buttonOn : Ui.button) && ok) speciesPick = s.id;
                    }
                    GUILayout.EndHorizontal();
                    if (Ui.Btn($"접종 (바이알 1, 배지 {d.capacityL}L)")) Result(sim.Inoculate(r, speciesPick), "접종했다. 72시간 후 수확. 배지는 하루 한 번 갈아야 한다.");
                    break;
                case ReactorState.Growing:
                case ReactorState.Ready:
                    GUILayout.BeginHorizontal();
                    if (Ui.Btn($"배지 교체 ({d.capacityL}L)")) Result(sim.ChangeMedium(r), "배지를 갈았다.");
                    if (Ui.Btn("온도·pH 점검")) SetHint(sim.Check(r));
                    if (Ui.Btn("수확", r.state == ReactorState.Ready))
                    {
                        string err; var lot = sim.Harvest(r, out err);
                        SetHint(lot != null ? $"{lot.kg:0.00}kg 수확. 작업대에서 가공하자. kg당 원가 {Ui.Money(sim.CostPerKg(lot))}" : err);
                    }
                    if (Ui.Btn("폐기")) Result(sim.Discard(r), "배치를 폐기했다.");
                    GUILayout.EndHorizontal();
                    Ui.S("점검 수치가 이상하면(pH 하락, 배지 탁함) 오염을 의심하고 폐기할지 정한다. 오염 배치를 팔면 사고로 이어질 수 있다.");
                    break;
                case ReactorState.Contaminated:
                    Ui.L("오염이 확인됐다. 폐기해야 한다.");
                    if (Ui.Btn("폐기")) Result(sim.Discard(r), "배치를 폐기했다.");
                    break;
            }
            End();
        }

        // ---------- 작업대 ----------

        void BenchPanel()
        {
            string loc = target.location;
            Begin($"{sim.LDef(loc).label} 작업대 · 수확·성형·포장", 680, 520);
            var job = sim.BenchAt(loc);
            if (job != null)
            {
                Ui.L($"가공 중: {sim.SDef(job.species).label} {sim.PDef(job.product).label} {job.packs}팩");
                Ui.Bar(1 - job.minutesLeft / job.totalMinutes, 300);
                Ui.S($"남은 시간 {job.minutesLeft:0}분. 자리를 비워도 계속 진행된다.");
            }
            Ui.S($"포장재 {sim.stock["packaging"]}개");
            Ui.H("수확물");
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(300));
            if (sim.lots.Count == 0) Ui.S("가공할 수확물이 없다. 리액터에서 수확하자.");
            foreach (var lot in sim.lots.ToList())
            {
                GUILayout.BeginVertical(Ui.box);
                Ui.L($"{sim.SDef(lot.species).label} {lot.kg:0.00}kg · {lot.q} · kg당 원가 {Ui.Money(sim.CostPerKg(lot))}");
                GUILayout.BeginHorizontal();
                foreach (var p in B.products)
                {
                    bool ok = sim.ProductUnlocked(p.id);
                    int n = sim.PacksFrom(lot, p.id);
                    string text = ok ? $"{p.label} {n}팩 ({n * p.minutesPerPack}분)" : $"{p.label} · {p.unlockLabel}";
                    if (Ui.Btn(text, ok && job == null)) Result(sim.StartBench(loc, lot.id, p.id), "가공을 시작했다.");
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
            End();
        }

        // ---------- 냉장고 ----------

        void FridgePanel()
        {
            Begin("냉장고", 760, 560);
            Ui.S($"냉장 보관 {B.sales.fridgeShelfHours / 24}일, 꺼낸 뒤 {B.sales.carryShelfHours}시간 안에 팔아야 한다. 소지 {sim.carried.Count}/{sim.carryLimit}팩");
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(360));
            Ui.H($"보관 중 {sim.fridge.Count}팩");
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(360));
            foreach (var grp in sim.fridge.GroupBy(p => p.species + "/" + p.product))
            {
                var first = grp.First();
                var pd = sim.PDef(first.product);
                GUILayout.BeginVertical(Ui.box);
                Ui.L($"{sim.SDef(first.species).label} {pd.label} × {grp.Count()}");
                Ui.S($"평균 품질 {grp.Average(p => p.q.Score(pd.textureWeight)):0} · 남은 기한 최소 {grp.Min(p => sim.HoursLeft(p)):0}시간");
                GUILayout.BeginHorizontal();
                if (Ui.Btn("1팩 꺼내기")) Result(sim.TakeFromFridge(grp.OrderBy(p => sim.HoursLeft(p)).First().id), "꺼냈다.");
                if (Ui.Btn("최대한 꺼내기"))
                {
                    string err = null;
                    foreach (var p in grp.OrderBy(p => sim.HoursLeft(p)).ToList()) { err = sim.TakeFromFridge(p.id); if (err != null) break; }
                    Result(err, "꺼냈다.");
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.BeginVertical();
            Ui.H($"들고 있는 팩 {sim.carried.Count}");
            foreach (var p in sim.carried.ToList())
            {
                GUILayout.BeginHorizontal();
                Ui.S($"{sim.SDef(p.species).label} {sim.PDef(p.product).label} · {sim.HoursLeft(p):0}시간");
                if (Ui.Btn("넣기", true, GUILayout.Width(60))) sim.PutInFridge(p.id);
                GUILayout.EndHorizontal();
            }
            if (sim.carried.Count > 0 && Ui.Btn("전부 넣기")) foreach (var p in sim.carried.ToList()) sim.PutInFridge(p.id);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            End();
        }

        // ---------- 지인 집 ----------

        void DoorPanel()
        {
            int c = ContactAtHouse(target.index);
            var cs = sim.contacts[c];
            var d = sim.CDef(c);
            Begin($"{d.name} ({d.relation})", 600, 420);
            Ui.S($"관계 {cs.relationship:0}/100 · 배달 {cs.deliveries}회" + (cs.regular ? " · 단골" : ""));
            var o = sim.OpenOrderFor(c);
            if (o != null)
            {
                Ui.L($"주문: {sim.SDef(o.species).label} {sim.PDef(o.product).label} {o.qty}팩 · 팩당 {Ui.Money(o.unitPrice)} · 기한 {sim.Fmt(o.deadline)}");
                int have = sim.carried.Count(p => p.product == o.product && p.species == o.species);
                Ui.S($"들고 있는 해당 제품 {have}팩");
                if (Ui.Btn($"전달하고 {Ui.Money(o.unitPrice * o.qty)} 받기"))
                {
                    string reply; var err = sim.Deliver(c, out reply);
                    if (err == null) lastReply = $"{d.name}: \"{reply}\""; else SetHint(err);
                }
            }
            else Ui.L("지금은 주문이 없다.");
            if (!string.IsNullOrEmpty(lastReply)) Ui.L(lastReply);
            End();
            if (panel == Panel.None) lastReply = "";
        }

        // ---------- 레스토랑 ----------

        void RestaurantPanel()
        {
            var ct = sim.contract;
            if (ct == null || !ct.active)
            {
                Begin("레스토랑", 560, 300);
                Ui.L("진행 중인 납품 계약이 없다. AI 컴퓨터의 판매 탭에서 계약할 수 있다.");
                End();
                return;
            }
            var o = ct.offer;
            Begin($"{o.name} 납품", 600, 380);
            Ui.L($"주간 {o.kgPerWeek}kg · 최소 품질 {o.minQuality} · kg당 {Ui.Money(o.pricePerKg)} · 미달 시 위약금 {Ui.Money(o.penalty)}");
            Ui.L($"이번 주 {ct.deliveredKg:0.0}/{o.kgPerWeek}kg · {ct.weeksDone + 1}/{o.weeks}주차 · 달성 {ct.weeksFulfilled}주");
            int ok = sim.carried.Count(p => p.q.Score(sim.PDef(p.product).textureWeight) >= o.minQuality);
            Ui.S($"들고 있는 팩 중 품질 기준을 넘는 것 {ok}팩");
            if (Ui.Btn("기준을 넘는 팩 전부 납품", ok > 0))
            {
                float kg; var err = sim.DeliverRestaurant(out kg);
                Result(err, $"{kg:0.00}kg 납품, {Ui.Money(kg * o.pricePerKg)} 입금");
            }
            End();
        }

        void InvestorPanel()
        {
            Begin("투자사 사무실", 600, 420);
            var r = sim.NextRoundDef;
            if (r == null) Ui.L("더 이상 진행할 라운드가 없다.");
            else
            {
                Ui.L($"다음 라운드: {r.label}");
                System.Collections.Generic.List<string> st;
                bool ok = sim.RoundConditionMet(r, out st);
                foreach (var s in st) Ui.S("· " + s);
                Ui.L(ok ? "조건을 채웠다. 곧 제안이 온다." : "아직 조건이 부족하다.");
                if (sim.investorDeadlineDay > 0) Ui.S($"투자자 기한: {sim.investorDeadlineDay}일차");
            }
            End();
        }

        // ---------- 택시 ----------

        void TaxiPanel()
        {
            Begin("자율주행 택시", 520, 300);
            Ui.S($"요금 {Ui.Money(B.costs.taxiFare)} · 이동 {B.costs.taxiMinutes}분");
            if (Ui.Btn("주택가 (집·차고·지인)", zone != "home")) Travel("home");
            if (Ui.Btn(sim.phase >= 2 ? "도심 (공유 실험실·레스토랑·투자사)" : "도심 · Phase 2부터", zone != "downtown" && sim.phase >= 2)) Travel("downtown");
            End();
        }

        // ---------- 면접 ----------

        Vector2 ivScroll;

        void InterviewPanel()
        {
            var c = sim.candidates.FirstOrDefault(x => x.id == interviewId);
            if (c == null) { panel = Panel.None; return; }
            Begin($"면접 · {c.name}", 900, 640);
            Ui.S($"남은 면접 시간 {sim.MinutesLeft(c)}분. 질문마다 노리는 능력치가 있고, 답을 들으면 추정 범위가 좁아진다. 답에는 잡음이 있다.");
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(380));
            Ui.H("이력서 주장 / 현재 추정");
            foreach (var s in LabMeat.Sim.Game.Stats)
            {
                var e = c.est[s];
                Ui.S($"{LabMeat.Sim.Game.StatLabel[s]}: 주장 {c.claims[s]} · 추정 {e[0]:0.0}–{e[1]:0.0}");
            }
            Ui.H("질문");
            foreach (var q in B.hiring.questions)
            {
                string tgt = q.kind == "career" ? "이력서 과장 여부" : string.Join(", ", q.targets.Select(t => LabMeat.Sim.Game.StatLabel[t]));
                bool can = c.inInterview && !c.asked.Contains(q.id) && q.minutes <= sim.MinutesLeft(c);
                if (Ui.Btn($"{q.text} ({q.minutes}분 · {tgt})", can))
                {
                    string ans; var err = sim.Ask(c.id, q.id, out ans);
                    if (err != null) SetHint(err);
                }
            }
            GUILayout.EndVertical();
            GUILayout.BeginVertical();
            Ui.H("대화");
            ivScroll = GUILayout.BeginScrollView(ivScroll, GUILayout.Height(380));
            foreach (var t in c.transcript) Ui.L(t);
            if (!string.IsNullOrEmpty(c.aiNote)) Ui.L(c.aiNote);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            if (c.inInterview)
            {
                if (Ui.Btn("면접 종료 (AI 분석 받기)")) sim.EndInterview(c.id);
            }
            else
            {
                Ui.S("검증·연봉 협상은 AI 컴퓨터 채용 탭에서 이어서 할 수 있다.");
                if (Ui.Btn("나가기")) panel = Panel.None;
            }
            GUILayout.EndArea();
        }

        // ---------- 정산 / 사건 / 메뉴 ----------

        void SettlementPanel()
        {
            var s = sim.lastSettlement;
            Begin($"{(s != null ? s.day : sim.day - 1)}일차 정산", 640, 600);
            if (s != null)
            {
                Ui.L($"매출 {Ui.Money(s.revenue)}");
                foreach (var kv in s.costs.OrderByDescending(k => k.Value)) Ui.S($"· {kv.Key} {Ui.Money(kv.Value)}");
                Ui.L($"비용 합계 {Ui.Money(s.costs.Values.Sum())} · 현금 {Ui.Money(s.cash)}");
                Ui.H("연구·사건");
                scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(240));
                if (s.notes.Count == 0) Ui.S("특이 사항 없음");
                foreach (var n in s.notes) Ui.S("· " + n);
                GUILayout.EndScrollView();
            }
            GUILayout.FlexibleSpace();
            if (Ui.Btn($"{sim.day}일차 아침 시작")) panel = Panel.None;
            GUILayout.EndArea();
        }

        void EventPanel()
        {
            var e = sim.pendingEvents.FirstOrDefault();
            if (e == null) { panel = Panel.None; return; }
            Begin(e.title, 640, 380);
            Ui.L(e.text);
            GUILayout.Space(12);
            for (int k = 0; k < e.options.Length; k++)
                if (Ui.Btn(e.options[k])) { sim.ResolveEvent(e, k); panel = sim.pendingEvents.Count > 0 ? Panel.Event : Panel.None; }
            GUILayout.EndArea();
        }

        void PausePanel()
        {
            Begin("일시정지", 420, 320);
            if (Ui.Btn("계속")) panel = Panel.None;
            GUILayout.BeginHorizontal();
            foreach (var v in new[] { 1, 2, 4 }) if (GUILayout.Button($"×{v}", speed == v ? Ui.buttonOn : Ui.button)) speed = v;
            GUILayout.EndHorizontal();
            if (Ui.Btn("새 게임")) NewGame();
            if (Ui.Btn("종료")) Application.Quit();
            GUILayout.EndArea();
        }

        void GameOverPanel()
        {
            var r = sim.records;
            Begin("게임 종료", 560, 420);
            Ui.L(sim.gameOverReason);
            Ui.H("기록");
            Ui.L($"누적 매출 {Ui.Money(r.revenue)}");
            Ui.L($"최저 달성 원가 {(r.lowestCostPerKg < 1e9 ? Ui.Money(r.lowestCostPerKg) + "/kg" : "없음")}");
            Ui.L($"진출 국가 {r.countries} · 누적 리콜 {r.recalls}회 · 판매 {r.packsSold}팩 · {sim.day}일 생존");
            if (Ui.Btn("새 게임")) NewGame();
            GUILayout.EndArea();
        }
    }
}
