// AI 컴퓨터(경영 화면)와 휴대 단말. 경영 판단은 컴퓨터 앞에서만 한다.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using LabMeat.Sim;
using SimGame = LabMeat.Sim.Game;

namespace LabMeat.Game
{
    public partial class GameManager
    {
        int compTab, phoneTab;
        readonly Dictionary<string, int> cart = new Dictionary<string, int>();
        readonly Dictionary<int, float> offerDraft = new Dictionary<int, float>();
        int selCandidate = -1;

        static readonly string[] CompTabs = { "연구", "리액터", "재료 주문", "판매", "재무", "채용", "뉴스", "AI 대화" };

        void ComputerPanel()
        {
            Begin($"AI 컴퓨터 · {sim.LDef(target != null ? target.location : "garage").label}", 1000, 680);
            compTab = Ui.Tabs(compTab, CompTabs);
            GUILayout.Space(6);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(500));
            switch (compTab)
            {
                case 0: ResearchTab(); break;
                case 1: ReactorTab(); break;
                case 2: OrderTab(); break;
                case 3: SalesTab(); break;
                case 4: FinanceTab(); break;
                case 5: HiringTab(); break;
                case 6: NewsTab(); break;
                case 7: AiTab(); break;
            }
            GUILayout.EndScrollView();
            End();
        }

        // ---------- 연구 ----------

        void ResearchTab()
        {
            var tier = B.compute[sim.computeTier];
            Ui.S($"예산은 매일 밤 정산 때 쓰이고, 결과는 확률적이다(실패 · 부분 성공 · 돌파). 연산 장비 {tier.label}: 연구 효율 ×{tier.researchMult}, AI 정확도 {tier.aiAccuracy:P0}. 오늘 예산 합계 {Ui.Money(sim.TotalResearchBudget)}/일");
            foreach (var d in B.research)
            {
                var a = sim.axes[d.id];
                GUILayout.BeginVertical(Ui.box);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{d.label}  Lv{a.level}/{d.maxLevel}", Ui.header, GUILayout.Width(220));
                Ui.Bar(a.progress, 120);
                GUILayout.Label(a.lastOutcome == Outcome.None ? "" : $"어젯밤: {a.lastNote}", Ui.small, GUILayout.Width(170));
                GUILayout.Label($"AI 추정 성공률 {a.aiEstimate:P0}", Ui.small, GUILayout.Width(150));
                GUILayout.EndHorizontal();
                Ui.S($"{d.problem} → {d.effect}");
                Ui.S($"이론: {d.theory}");
                GUILayout.BeginHorizontal();
                GUILayout.Label($"예산 {Ui.Money(a.budget)}/일", Ui.label, GUILayout.Width(140));
                foreach (var step in new[] { -500, -100, 100, 500 })
                    if (Ui.Btn((step > 0 ? "+" : "") + step, true, GUILayout.Width(64))) sim.SetBudget(d.id, a.budget + step);
                if (Ui.Btn("0", true, GUILayout.Width(40))) sim.SetBudget(d.id, 0);
                float staff = sim.StaffResearchPoints(d.id);
                GUILayout.Label(staff > 0 ? $"연구원 +{staff:0}점/일" : "", Ui.small);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            Ui.H("연산 장비");
            for (int k = sim.computeTier + 1; k < B.compute.Count; k++)
            {
                var c = B.compute[k];
                if (Ui.Btn($"{c.label} 구매 {Ui.Money(c.price)} · 유지 {Ui.Money(c.perDay)}/일 · 효율 ×{c.researchMult} · AI 정확도 {c.aiAccuracy:P0}" + (sim.phase < c.phase ? $" (Phase {c.phase})" : ""), sim.phase >= c.phase))
                    Result(sim.BuyCompute(k), "연산 장비를 업그레이드했다.");
            }
        }

        // ---------- 리액터 ----------

        void ReactorTab()
        {
            foreach (var r in sim.reactors)
            {
                var d = sim.RDef(r.def);
                GUILayout.BeginVertical(Ui.box);
                Ui.L($"{sim.LDef(r.location).label} {r.slot + 1}번 · {d.label} · {StateText(r)}");
                if (!string.IsNullOrEmpty(r.lastCheck)) Ui.S("최근 점검: " + r.lastCheck);
                if (r.state != ReactorState.Empty) Ui.S($"배치 비용 {Ui.Money(r.batchCost)} · 시작 {sim.Fmt(r.startMinute)}");
                GUILayout.EndVertical();
            }
            Ui.S("조작(접종·배지 교체·점검·수확)은 리액터 앞에서 직접 한다.");
            Ui.H("리액터 구매");
            foreach (var d in B.reactors)
            {
                foreach (var loc in B.locations)
                {
                    if (d.locked == 1) { Ui.S($"{d.label} · v0.2 이후"); break; }
                    if (d.id == "pilot" && loc.id == "garage") continue;
                    int used = sim.reactors.Count(r => r.location == loc.id);
                    bool ok = sim.phase >= d.phase && sim.phase >= loc.phase;
                    string text = $"{d.label} → {loc.label} ({used}/{loc.reactorSlots}) · {Ui.Money(d.price)} · {d.capacityL}L · {d.cultureHours}시간 · 기본 {d.baseYieldKg}kg" + (ok ? "" : $" · Phase {Mathf.Max(d.phase, loc.phase)}");
                    if (Ui.Btn(text, ok)) Result(sim.BuyReactor(d.id, loc.id), "리액터를 들였다.");
                }
            }
        }

        // ---------- 재료 주문 ----------

        void OrderTab()
        {
            Ui.S($"주문하면 다음 날 {B.time.parcelArrivalHour}시에 집 앞 택배함에 도착한다. 직접 가서 찾아야 한다.");
            foreach (var m in B.materials)
            {
                if (!cart.ContainsKey(m.id)) cart[m.id] = 0;
                GUILayout.BeginHorizontal(Ui.box);
                GUILayout.Label($"{m.label} · 재고 {sim.stock[m.id]}{m.unit} · {Ui.Money(sim.MaterialPrice(m.id))}/{m.unit}", Ui.label, GUILayout.Width(420));
                if (Ui.Btn("−", true, GUILayout.Width(40))) cart[m.id] = Mathf.Max(0, cart[m.id] - m.orderStep);
                GUILayout.Label($"{cart[m.id]}{m.unit}", Ui.header, GUILayout.Width(70));
                if (Ui.Btn("+", true, GUILayout.Width(40))) cart[m.id] += m.orderStep;
                if (Ui.Btn($"+{m.orderStep * 5}", true, GUILayout.Width(60))) cart[m.id] += m.orderStep * 5;
                GUILayout.EndHorizontal();
            }
            float cost = sim.OrderCost(cart);
            if (Ui.Btn($"주문 {Ui.Money(cost)}", cost > 0))
            {
                var err = sim.OrderMaterials(new Dictionary<string, int>(cart));
                if (err == null) { foreach (var k in cart.Keys.ToList()) cart[k] = 0; }
                Result(err, "주문했다. 내일 택배함을 확인하자.");
            }
            int waiting = sim.parcels.Count;
            if (waiting > 0) Ui.S($"배송 중·미수령 택배 {waiting}건");
        }

        // ---------- 판매 ----------

        void SalesTab()
        {
            Ui.H("판매 가격 (닭 기준, 팩당)");
            foreach (var p in B.products.Where(p => p.locked == 0))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{p.label} · 기준 {Ui.Money(p.referencePrice)}" + (sim.ProductUnlocked(p.id) ? "" : $" · {p.unlockLabel}"), Ui.label, GUILayout.Width(380));
                if (Ui.Btn("−", true, GUILayout.Width(40))) sim.prices[p.id] = Mathf.Max(1, sim.prices[p.id] - 1);
                GUILayout.Label(Ui.Money(sim.prices[p.id]), Ui.header, GUILayout.Width(70));
                if (Ui.Btn("+", true, GUILayout.Width(40))) sim.prices[p.id] += 1;
                GUILayout.EndHorizontal();
            }
            Ui.S("비싸면 주문이 줄고, 싸면 현금이 마른다. 돼지·소는 종별 배수가 붙는다.");

            Ui.H("열린 주문");
            if (sim.OpenOrders.Count == 0) Ui.S("없음");
            foreach (var o in sim.OpenOrders)
            {
                GUILayout.BeginHorizontal();
                Ui.L($"{sim.CDef(o.contact).name}: {sim.SDef(o.species).label} {sim.PDef(o.product).label} {o.qty}팩 · {Ui.Money(o.unitPrice * o.qty)} · 기한 {sim.Fmt(o.deadline)}");
                if (Ui.Btn("거절", true, GUILayout.Width(70))) sim.DeclineOrder(o.id);
                GUILayout.EndHorizontal();
            }

            Ui.H("연락처");
            for (int k = 0; k < sim.contacts.Count; k++)
            {
                var c = sim.contacts[k];
                if (!c.unlocked) continue;
                var d = sim.CDef(k);
                Ui.S($"{d.name} ({d.relation}) · 관계 {c.relationship:0} · 배달 {c.deliveries}회{(c.regular ? " · 단골" : "")} · 선호 {sim.PDef(d.likes).label}");
            }
            Ui.S($"모르는 사람 {sim.contacts.Count(c => !c.unlocked)}명은 입소문으로만 연결된다.");

            Ui.H("레스토랑 주간 계약");
            if (sim.phase < 2) Ui.S("Phase 2부터");
            else
            {
                if (sim.contract != null && sim.contract.active)
                {
                    var ct = sim.contract;
                    Ui.L($"{ct.offer.name}: 이번 주 {ct.deliveredKg:0.0}/{ct.offer.kgPerWeek}kg · {ct.weeksDone + 1}/{ct.offer.weeks}주차 · 달성 {ct.weeksFulfilled}주");
                }
                for (int k = 0; k < B.restaurant.offers.Count; k++)
                {
                    var o = B.restaurant.offers[k];
                    if (Ui.Btn($"{o.name}: 주 {o.kgPerWeek}kg, 품질 {o.minQuality}+, kg당 {Ui.Money(o.pricePerKg)}, 위약금 {Ui.Money(o.penalty)}, {o.weeks}주", sim.contract == null || !sim.contract.active))
                        Result(sim.AcceptContract(k), "계약했다. 도심 레스토랑 거리로 직접 납품한다.");
                }
            }

            Ui.H($"수출 · {B.approval.country}");
            if (sim.approval != ApprovalState.Approved) Ui.S("국가 승인 후 가능");
            else if (Ui.Btn($"냉장고 재고 수출 (팩당 {Ui.Money(B.sales.exportPricePerPack)}, 오늘 남은 한도 {B.sales.exportDailyCap - sim.exportedToday}팩)"))
                Result(sim.Export(B.sales.exportDailyCap), "수출했다.");
        }

        // ---------- 재무 ----------

        void FinanceTab()
        {
            Ui.L($"현금 {Ui.Money(sim.cash)} · 창업자 지분 {sim.equity:0.0}% · 여론 {sim.opinion:0}/100 · Phase {sim.phase}");
            var s = sim.lastSettlement;
            if (s != null) Ui.S($"어제 매출 {Ui.Money(s.revenue)}, 비용 {Ui.Money(s.costs.Values.Sum())}");
            float fixedDaily = B.costs.livingPerDay + sim.reactors.Count * B.costs.energyPerReactorPerDay + B.compute[sim.computeTier].perDay
                + B.locations.Where(l => sim.phase >= l.phase).Sum(l => l.rentPerDay) + sim.employees.Sum(e => e.salary) + sim.TotalResearchBudget;
            Ui.S($"고정 지출(생활비·에너지·장비·임대·인건비·연구) 하루 약 {Ui.Money(fixedDaily)}. 이 속도면 재료비 제외 {(fixedDaily > 0 ? sim.cash / fixedDaily : 0):0}일 버틴다.");

            Ui.H("다음 투자 라운드");
            var r = sim.NextRoundDef;
            if (r == null) Ui.S("없음");
            else
            {
                List<string> st; sim.RoundConditionMet(r, out st);
                Ui.L($"{r.label}: {Ui.Money(r.amount * sim.roundAmountMult)}, 지분 {r.equity}% → Phase {r.toPhase}");
                foreach (var x in st) Ui.S("· " + x);
                if (sim.investorDeadlineDay > 0) Ui.S($"투자자 기한 {sim.investorDeadlineDay}일차 (남은 {sim.investorDeadlineDay - sim.day}일)");
            }

            Ui.H("여론");
            if (Ui.Btn($"마케팅 캠페인 {Ui.Money(B.costs.marketingCost)} (여론 상승, 높을수록 효과 감소)")) Result(sim.Marketing(), "캠페인을 집행했다.");

            Ui.H($"국가 승인 · {B.approval.country} (개방형 도시국가)");
            switch (sim.approval)
            {
                case ApprovalState.None:
                    if (Ui.Btn($"판매 승인 신청 {Ui.Money(B.approval.fee)} · 기본 {B.approval.baseDays}일, 여론이 높을수록 빠름" + (sim.phase < B.approval.phase ? $" (Phase {B.approval.phase})" : ""), sim.phase >= B.approval.phase))
                        Result(sim.ApplyApproval(), "심사를 신청했다.");
                    break;
                case ApprovalState.Reviewing:
                    Ui.L($"심사 중 {sim.approvalProgressDays:0.0}/{sim.approvalRequiredDays:0.0}일");
                    Ui.Bar(sim.approvalProgressDays / sim.approvalRequiredDays, 300);
                    Ui.S("심사 중 오염 사고가 나면 지연된다.");
                    break;
                case ApprovalState.Approved: Ui.L("승인 완료. 판매 탭에서 수출할 수 있다."); break;
            }

            Ui.H("기록");
            var rec = sim.records;
            Ui.S($"누적 매출 {Ui.Money(rec.revenue)} · 최저 원가 {(rec.lowestCostPerKg < 1e9 ? Ui.Money(rec.lowestCostPerKg) + "/kg" : "없음")} · 판매 {rec.packsSold}팩 · 최근 10배치 성공률 {rec.BatchSuccess(10):P0} · 리콜 {rec.recalls}회");
        }

        // ---------- 채용 ----------

        void HiringTab()
        {
            if (sim.phase < B.hiring.phase) { Ui.L($"직원은 Phase {B.hiring.phase}(공유 실험실)부터 고용할 수 있다."); return; }
            Ui.S("능력치는 숨겨져 있다. 이력서는 과장될 수 있고, 면접 질문으로 추정 범위를 좁힌다. 면접은 공유 실험실에서 대면으로 한다.");
            Ui.H($"지원자 {sim.candidates.Count}명");
            foreach (var c in sim.candidates.ToList())
            {
                GUILayout.BeginVertical(Ui.box);
                Ui.L($"{c.name} · 요구 일급 약 {Ui.Money(Mathf.Round(c.asking / 10) * 10)} · 결정 기한 {c.offerDeadlineDay}일차" + (c.interviewMinute >= 0 && !c.interviewed ? $" · 면접 {sim.Fmt(c.interviewMinute)}" : c.interviewed ? " · 면접 완료" : ""));
                Ui.S(string.Join(" · ", SimGame.Stats.Select(s => $"{SimGame.StatLabel[s]} 주장 {c.claims[s]} / 추정 {c.est[s][0]:0}–{c.est[s][1]:0}")));
                if (!string.IsNullOrEmpty(c.aiNote)) Ui.S(c.aiNote);
                GUILayout.BeginHorizontal();
                if (c.interviewMinute < 0)
                {
                    long now = sim.T;
                    long in2h = now + 120 - now % 60;
                    long tom10 = (long)sim.day * 1440 + 10 * 60, tom14 = (long)sim.day * 1440 + 14 * 60;
                    if (sim.Hour < 16 && Ui.Btn($"오늘 {(in2h - sim.DayStart) / 60}:00 면접")) Result(sim.ScheduleInterview(c.id, in2h), "면접을 잡았다.");
                    if (Ui.Btn("내일 10:00 면접")) Result(sim.ScheduleInterview(c.id, tom10), "면접을 잡았다.");
                    if (Ui.Btn("내일 14:00 면접")) Result(sim.ScheduleInterview(c.id, tom14), "면접을 잡았다.");
                }
                if (c.interviewed)
                {
                    if (Ui.Btn($"실기 테스트 {Ui.Money(B.hiring.practicalCost)} · {B.hiring.practicalMinutes / 60}시간", !c.practicalDone)) Result(sim.PracticalTest(c.id), "실기 테스트를 했다.");
                    if (Ui.Btn($"평판 조회 {Ui.Money(B.hiring.referenceCost)} · 하루", !c.referenceDone && !c.referencePending)) Result(sim.ReferenceCheck(c.id), "전 직장에 연락했다. 내일 결과가 온다.");
                }
                GUILayout.EndHorizontal();
                if (c.interviewed)
                {
                    float draft;
                    if (!offerDraft.TryGetValue(c.id, out draft)) draft = Mathf.Round(c.asking / 10) * 10;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"제안 일급 {Ui.Money(draft)}", Ui.label, GUILayout.Width(170));
                    if (Ui.Btn("−10", true, GUILayout.Width(50))) draft -= 10;
                    if (Ui.Btn("+10", true, GUILayout.Width(50))) draft += 10;
                    offerDraft[c.id] = draft;
                    if (Ui.Btn("제안하기", true, GUILayout.Width(110)))
                    {
                        bool acc; var err = sim.Offer(c.id, draft, out acc);
                        Result(err, acc ? "입사를 수락했다." : "거절당했다.");
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
            }

            Ui.H($"직원 {sim.employees.Count}명");
            foreach (var e in sim.employees.ToList())
            {
                GUILayout.BeginVertical(Ui.box);
                Ui.L($"{e.name} · 일급 {Ui.Money(e.salary)} · 근무 {e.daysWorked}일 · {SimGame.RoleLabel[e.role]}" + (e.role == "research" ? $"({sim.ADef(e.axis).label})" : "") + $" · {sim.LDef(e.location).label}" + (e.absentToday ? " · 오늘 결근" : ""));
                Ui.S(string.Join(" · ", SimGame.Stats.Select(s => $"{SimGame.StatLabel[s]} {e.est[s][0]:0}–{e.est[s][1]:0}")) + (e.knownTraits.Count > 0 ? " · 특성: " + string.Join(", ", e.knownTraits.Select(t => sim.TDef(t).label)) : ""));
                GUILayout.BeginHorizontal();
                foreach (var role in SimGame.RoleLabel)
                    if (GUILayout.Button(role.Value, e.role == role.Key ? Ui.buttonOn : Ui.button)) sim.SetRole(e.id, role.Key);
                if (e.role == "research")
                    foreach (var a in B.research)
                        if (GUILayout.Button(a.label, e.axis == a.id ? Ui.buttonOn : Ui.button)) sim.SetRole(e.id, "research", a.id);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                foreach (var l in B.locations.Where(l => sim.phase >= l.phase))
                    if (GUILayout.Button(l.label, e.location == l.id ? Ui.buttonOn : Ui.button)) sim.SetEmployeeLocation(e.id, l.id);
                if (Ui.Btn("해고", true, GUILayout.Width(70))) sim.Fire(e.id);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            Ui.S("배양 담당은 그 장소의 리액터를 돌보고(배지 교체·수확·재접종·오염 발견), 가공 담당은 수확물을 포장해 냉장고에 넣고, 연구 담당은 배정된 축에 연구 점수를 더한다. 근무 시간 9–18시.");
        }

        // ---------- 뉴스 / AI ----------

        void NewsTab()
        {
            if (sim.newsFeed.Count == 0) Ui.S("아직 뉴스가 없다.");
            foreach (var n in Enumerable.Reverse(sim.newsFeed).Take(40)) Ui.L($"{sim.Fmt(n.minute)} · {n.headline}");
        }

        void AiTab()
        {
            Ui.L("AI 연구 보조");
            Ui.L(string.IsNullOrEmpty(sim.aiAdvice) ? "추천 없음" : sim.aiAdvice);
            Ui.S("AI의 제안은 항상 맞지 않는다. 연산 장비를 올리면 정확도가 오른다.");
            if (!string.IsNullOrEmpty(sim.aiAdviceAxis) && Ui.Btn("추천대로: 해당 축 예산 +$300/일"))
                sim.SetBudget(sim.aiAdviceAxis, sim.axes[sim.aiAdviceAxis].budget + 300);
            Ui.H("현재 상황 요약");
            int ready = sim.reactors.Count(r => r.state == ReactorState.Ready);
            int stale = sim.reactors.Count(r => r.state == ReactorState.Growing && r.freshness < 40);
            if (ready > 0) Ui.S($"· 수확 가능한 리액터 {ready}기");
            if (stale > 0) Ui.S($"· 배지 교체가 필요한 리액터 {stale}기");
            if (sim.lots.Count > 0) Ui.S($"· 가공 대기 수확물 {sim.lots.Count}건");
            if (sim.OpenOrders.Count > 0) Ui.S($"· 열린 주문 {sim.OpenOrders.Count}건");
            if (sim.ArrivedParcels.Count > 0) Ui.S("· 택배함에 도착한 재료가 있다");
            int medNeed = sim.reactors.Sum(r => sim.RDef(r.def).capacityL);
            if (sim.stock["medium"] < medNeed) Ui.S($"· 배지 재고가 하루치({medNeed}L)보다 적다");
        }

        // ---------- 휴대 단말 ----------

        static readonly string[] PhoneTabs = { "메시지", "주문", "리액터", "뉴스" };

        void PhonePanel()
        {
            var r = new Rect(Ui.VW - 460, Ui.VH - 620, 440, 600);
            GUILayout.BeginArea(r, Ui.panel);
            GUILayout.Label($"휴대 단말 · {sim.ClockText}", Ui.title);
            phoneTab = Ui.Tabs(phoneTab, PhoneTabs);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(440));
            switch (phoneTab)
            {
                case 0:
                    foreach (var m in Enumerable.Reverse(sim.messages).Take(40)) { Ui.S($"{sim.Fmt(m.minute)} · {m.from}"); Ui.L(m.text); }
                    break;
                case 1:
                    if (sim.OpenOrders.Count == 0) Ui.S("열린 주문 없음");
                    foreach (var o in sim.OpenOrders)
                        Ui.L($"{sim.CDef(o.contact).name} · {sim.SDef(o.species).label} {sim.PDef(o.product).label} {o.qty}팩 · 기한 {sim.Fmt(o.deadline)}");
                    if (sim.contract != null && sim.contract.active) Ui.L($"{sim.contract.offer.name}: 이번 주 {sim.contract.deliveredKg:0.0}/{sim.contract.offer.kgPerWeek}kg");
                    Ui.S($"소지 {sim.carried.Count}팩 · 냉장고 {sim.fridge.Count}팩");
                    break;
                case 2:
                    foreach (var re in sim.reactors) Ui.L($"{sim.LDef(re.location).label} {re.slot + 1} · {StateText(re)}");
                    Ui.S("오염 여부는 현장에서 점검해야 알 수 있다.");
                    break;
                case 3:
                    foreach (var n in Enumerable.Reverse(sim.newsFeed).Take(20)) Ui.L($"{sim.Fmt(n.minute)} · {n.headline}");
                    break;
            }
            GUILayout.EndScrollView();
            if (Ui.Btn("닫기 (Tab)")) panel = Panel.None;
            GUILayout.EndArea();
        }
    }
}
