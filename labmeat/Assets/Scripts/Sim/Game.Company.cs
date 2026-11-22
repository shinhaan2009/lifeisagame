// 회사: 투자 라운드와 단계(Phase) 관문, 국가 승인, 여론, 사건 이벤트, 뉴스 피드.
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabMeat.Sim
{
    public partial class Game
    {
        public int nextRound = 1;
        public int roundRetryDay;
        public bool roundOfferPending;
        public int investorDeadlineDay = -1;
        public float roundAmountMult = 1f;

        public ApprovalState approval = ApprovalState.None;
        public float approvalProgressDays, approvalRequiredDays;

        public List<NewsItem> newsFeed = new List<NewsItem>();
        public List<Buff> buffs = new List<Buff>();
        List<int> concealChecks = new List<int>();   // 은폐 발각 판정 예정일

        static readonly string[] Rivals = { "셀라미트", "프라임셀", "뉴테이블", "바이오컷", "오리진푸드" };

        void InitCompany() { }

        // ---------- 투자 라운드 ----------

        public RoundDef NextRoundDef { get { return nextRound < B.rounds.Count ? B.rounds[nextRound] : null; } }

        public bool RoundConditionMet(RoundDef r, out List<string> status)
        {
            status = new List<string>();
            bool ok = true;
            if (r.packsSold > 0) { bool c = records.packsSold >= r.packsSold; ok &= c; status.Add($"누적 판매 {records.packsSold}/{r.packsSold}팩"); }
            if (r.regulars > 0) { bool c = RegularCount >= r.regulars; ok &= c; status.Add($"단골 {RegularCount}/{r.regulars}명"); }
            if (r.restaurantWeeks > 0) { bool c = TotalRestaurantWeeks >= r.restaurantWeeks; ok &= c; status.Add($"레스토랑 주간 납품 달성 {TotalRestaurantWeeks}/{r.restaurantWeeks}주"); }
            if (r.batchSuccess > 0)
            {
                float s = records.BatchSuccess(10);
                bool c = records.batches.Count >= 10 && s >= r.batchSuccess; ok &= c;
                status.Add($"최근 10배치 성공률 {s:P0}/{r.batchSuccess:P0} ({Math.Min(10, records.batches.Count)}/10배치)");
            }
            if (r.approvals > 0) { bool c = records.countries >= r.approvals; ok &= c; status.Add($"국가 승인 {records.countries}/{r.approvals}곳"); }
            if (r.toPhase > 4 || r.condition == "locked") { ok = false; status.Add("v0.2 이후"); }
            return ok;
        }

        void CheckRounds()
        {
            var r = NextRoundDef;
            if (r == null || roundOfferPending || day < roundRetryDay) return;
            List<string> st;
            if (!RoundConditionMet(r, out st)) return;
            roundOfferPending = true;
            float amount = (float)Math.Round(r.amount * roundAmountMult / 1000f) * 1000f;
            Raise(new GameEvent
            {
                kind = "round", title = $"{r.label} 제안",
                text = $"{r.condition}을(를) 달성한 것을 본 투자자가 ${amount:N0}에 지분 {r.equity}%를 제안했다. 수락하면 Phase {r.toPhase}가 열리고, 다음 마일스톤 기한이 생긴다.",
                options = new[] { "수락한다", "지금은 거절한다 (7일 후 재제안)" }, amount = amount,
            });
        }

        void AcceptRound(float amount)
        {
            var r = NextRoundDef;
            cash += amount;
            equity *= 1f - r.equity / 100f;
            phase = Math.Max(phase, r.toPhase);
            nextRound++;
            investorDeadlineDay = r.deadlineDays > 0 ? day + r.deadlineDays : -1;
            AddNews("company", $"배양육 스타트업, {r.label} ${amount:N0} 유치");
            if (r.toPhase == 2) Notify("AI", "Phase 2: 도심이 열렸습니다. 공유 실험실에서 파일럿 리액터를 들이고 직원을 뽑을 수 있습니다. 택시로 이동하세요.");
            if (r.toPhase >= 3) Notify("AI", "Phase 3 진입 판정 통과. 산업단지와 파일럿 공장은 v0.2에서 제공됩니다. 게임은 계속됩니다.");
        }

        // ---------- 국가 승인 ----------

        public string ApplyApproval()
        {
            var a = B.approval;
            if (phase < a.phase) return $"Phase {a.phase}부터";
            if (approval != ApprovalState.None) return "이미 신청했다";
            if (!CanAfford(a.fee)) return "현금 부족";
            Spend("규제·승인", a.fee);
            approval = ApprovalState.Reviewing;
            approvalProgressDays = 0;
            approvalRequiredDays = a.baseDays / (0.5f + opinion / 100f);
            Notify("규제 기관", $"{a.country} 판매 승인 심사를 시작합니다. 예상 {approvalRequiredDays:0}일.");
            return null;
        }

        void DelayApproval(float days, string why)
        {
            if (approval != ApprovalState.Reviewing) return;
            approvalRequiredDays += days;
            Notify("규제 기관", $"{why}: 심사가 {days:0}일 지연됩니다.");
        }

        // ---------- 분 단위 ----------

        void StepCompany()
        {
            if (approval == ApprovalState.Reviewing)
            {
                approvalProgressDays += 1f / 1440f;
                if (approvalProgressDays >= approvalRequiredDays)
                {
                    approval = ApprovalState.Approved;
                    records.countries = Math.Max(records.countries, 1);
                    opinion = Math.Min(100, opinion + 5);
                    AddNews("company", $"{B.approval.country}, 국내 스타트업 배양육 판매 승인");
                    Notify("규제 기관", $"{B.approval.country} 판매 승인. AI 컴퓨터에서 수출할 수 있습니다.");
                }
            }
            if (T % 60 == 0)
            {
                CheckRounds();
                if (rng.Chance(B.events.newsPerDay / 24f)) RandomNews();
            }
        }

        void DailyCompany()
        {
            buffs.RemoveAll(b => b.untilDay < day + 1);
            opinion += (50f - opinion) * 0.03f;   // 여론은 시간이 지나면 중립으로 서서히 돌아간다
            if (investorDeadlineDay > 0 && day > investorDeadlineDay && NextRoundDef != null)
            {
                roundAmountMult *= 0.85f;
                opinion = Math.Max(0, opinion - 3);
                investorDeadlineDay = day + 20;
                ledger.notes.Add("투자자 기한 초과: 다음 라운드 조건이 나빠졌다");
                Raise(new GameEvent { kind = "info", title = "투자자 압박", text = "약속한 마일스톤 기한을 넘겼다. 투자자들이 불안해한다. 다음 라운드 금액이 줄어들고 기한이 20일 연장된다.", options = new[] { "확인" } });
            }
            if (approval == ApprovalState.Reviewing && rng.Chance(B.events.lobbyChancePerDay))
            {
                Raise(new GameEvent
                {
                    kind = "lobby", title = "축산업계 로비",
                    text = $"축산업계가 {B.approval.country} 규제 기관에 배양육 심사 강화를 요구하는 로비를 시작했다.",
                    options = new[] { $"로비 대응 비용 ${B.events.lobbyCost:N0}를 쓴다", $"무시한다 (심사 {B.approval.lobbyDelayDays:0}일 지연, 여론 하락)" },
                });
            }
            for (int k = concealChecks.Count - 1; k >= 0; k--)
            {
                if (day >= concealChecks[k])
                {
                    concealChecks.RemoveAt(k);
                    opinion = Math.Max(0, opinion - B.events.concealOpinion);
                    records.recalls++;
                    DelayApproval(B.approval.contamDelayDays * 2, "은폐 사실 보도");
                    AddNews("safety", "배양육 스타트업, 오염 사고 은폐 의혹 보도");
                    Raise(new GameEvent { kind = "info", title = "은폐 발각", text = $"숨겼던 오염 사고가 보도됐다. 여론이 {B.events.concealOpinion:0} 하락하고 리콜을 진행한다.", options = new[] { "확인" } });
                }
            }
            if (phase < 3) CheckRounds();
        }

        // ---------- 사건 처리 ----------

        public void ResolveEvent(GameEvent e, int option)
        {
            pendingEvents.Remove(e);
            switch (e.kind)
            {
                case "round":
                    roundOfferPending = false;
                    if (option == 0) AcceptRound(e.amount); else roundRetryDay = day + 7;
                    break;
                case "illness":
                    if (option == 0)
                    {
                        opinion = Math.Max(0, opinion - B.events.discloseOpinion);
                        Spend("리콜·환불", e.amount);
                        records.recalls++;
                        DelayApproval(B.approval.contamDelayDays, "오염 사고");
                        AddNews("safety", "배양육 스타트업, 오염 배치 자진 리콜");
                    }
                    else if (rng.Chance(B.events.concealDiscoverChance))
                    {
                        concealChecks.Add(day + rng.Int(2, 10));
                    }
                    break;
                case "lobby":
                    if (option == 0) { if (CanAfford(B.events.lobbyCost)) Spend("로비 대응", B.events.lobbyCost); else { DelayApproval(B.approval.lobbyDelayDays, "로비 대응 실패"); } }
                    else { DelayApproval(B.approval.lobbyDelayDays, "축산업계 로비"); opinion = Math.Max(0, opinion - B.events.ignoreLobbyOpinion); }
                    break;
                case "raise":
                    ResolveRaise(e.data, option == 0);
                    break;
            }
        }

        public string Marketing()
        {
            if (!CanAfford(B.costs.marketingCost)) return "현금 부족";
            Spend("마케팅", B.costs.marketingCost);
            float gain = B.costs.marketingOpinion * (1f - opinion / 120f);
            opinion = Math.Min(100, opinion + gain);
            return null;
        }

        public string TakeTaxi()
        {
            if (!CanAfford(B.costs.taxiFare)) return "현금 부족";
            Spend("이동", B.costs.taxiFare);
            Advance(B.costs.taxiMinutes);
            return null;
        }

        // ---------- 뉴스 ----------

        public void AddNews(string kind, string headline)
        {
            newsFeed.Add(new NewsItem { minute = T, kind = kind, headline = headline });
            if (newsFeed.Count > 80) newsFeed.RemoveAt(0);
            toasts.Enqueue("[뉴스] " + headline);
        }

        void RandomNews()
        {
            var t = rng.Pick(B.news);
            string h = t.text.Replace("{rival}", rng.Pick(Rivals)).Replace("{country}", B.approval.country).Replace("{n}", rng.Int(2, 9).ToString());
            switch (t.effect)
            {
                case "research":
                    buffs.Add(new Buff { kind = "research", axis = t.axis, mult = B.researchModel.newsBuffMult, untilDay = day + B.researchModel.newsBuffDays });
                    break;
                case "opinion":
                    opinion = Math.Max(0, Math.Min(100, opinion + t.value));
                    break;
                case "reference":
                    referenceMult = Math.Min(1.6f, referenceMult * t.value);
                    break;
                case "approval":
                    if (approval == ApprovalState.Reviewing) approvalRequiredDays = Math.Max(approvalProgressDays + 1, approvalRequiredDays - t.value);
                    break;
            }
            AddNews(t.kind, h);
        }
    }
}
