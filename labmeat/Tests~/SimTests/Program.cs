// 시뮬레이션 코어 테스트 + 스크립트 플레이어 밸런스 점검.
// 실행: dotnet run --project Tests~/SimTests [-- bot]
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LabMeat.Sim;

static class Program
{
    static int failed, passed;
    static string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    static Balance Load() { return Json.Parse<Balance>(File.ReadAllText(Path.Combine(Root, "Assets/Resources/balance.json"))); }

    static void Check(bool ok, string name, string detail = "")
    {
        if (ok) { passed++; Console.WriteLine("ok   " + name); }
        else { failed++; Console.WriteLine("FAIL " + name + " " + detail); }
    }

    static int Main(string[] args)
    {
        if (args.Contains("bot")) { Bot.Run(Load(), args.Contains("-v")); return 0; }
        Tests();
        Console.WriteLine($"\n{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    static void Tests()
    {
        var B = Load();
        Check(B.research.Count == 5, "연구 축 5개");
        Check(B.reactors.Count == 3, "리액터 규모 3단계");

        {
            var g = new Game(B, 1);
            Check(g.ClockText == "07:00" && g.day == 1, "07:00 시작");
            g.Advance(60);
            Check(g.ClockText == "08:00", "1분 틱 × 60 = 1시간");
            Check(!g.CanSleep, "18시 전에는 잠들 수 없다");
        }
        {
            var g = new Game(B, 2);
            var r = g.reactors[0];
            Check(g.Inoculate(r, "chicken") == null && r.state == ReactorState.Growing, "접종");
            Check(g.Inoculate(r, "beef") != null, "이미 배양 중이면 접종 불가");
            g.Advance(30);
            Check(r.freshness < 100 && r.freshness > 97, "배지 신선도 감소", r.freshness.ToString());
            float before = g.stock["medium"];
            Check(g.ChangeMedium(r) == null && r.freshness == 100 && g.stock["medium"] == before - 5, "배지 교체");
        }
        {
            // 배양 72시간 → 수확 → 가공 → 냉장고
            var g = new Game(B, 3);
            var r = g.reactors[0];
            g.stock["medium"] = 100;
            g.Inoculate(r, "chicken");
            for (int h = 0; h < 80; h++) { g.Advance(60); if (r.freshness < 50 && g.stock["medium"] >= 5) g.ChangeMedium(r); r.contaminated = false; }
            Check(r.state == ReactorState.Ready, "72시간 후 수확 가능", r.state + " " + r.growth);
            string err; var lot = g.Harvest(r, out err);
            Check(lot != null && lot.kg > 1.0f, "수확량", lot == null ? err : lot.kg.ToString());
            if (lot == null) return;
            int packs = g.PacksFrom(lot, "ground");
            Check(g.StartBench("garage", lot.id, "ground") == null, "작업대 가공 시작");
            g.Advance(packs * 6 + 1);
            Check(g.fridge.Count == packs, "가공 완료 → 냉장고", g.fridge.Count + "/" + packs);
            Check(g.StartBench("garage", 999, "nugget") != null, "잠긴 제품/없는 수확물 거부");
        }
        {
            // 배달: 주문 → 들고 가서 전달 → 결제
            var g = new Game(B, 4);
            g.fridge.Add(new Pack { id = 9001, product = "ground", species = "chicken", q = new Quality { taste = 60, texture = 40, safety = 90 }, madeMinute = g.T });
            g.fridge.Add(new Pack { id = 9002, product = "ground", species = "chicken", q = new Quality { taste = 60, texture = 40, safety = 90 }, madeMinute = g.T });
            g.orders.Add(new Order { id = 1, contact = 0, product = "ground", species = "chicken", qty = 2, unitPrice = 20, created = g.T, deadline = g.T + 600, status = OrderStatus.Open });
            string reply;
            Check(g.Deliver(0, out reply) != null, "들고 있지 않으면 배달 불가");
            g.TakeFromFridge(9001); g.TakeFromFridge(9002);
            float cash = g.cash;
            Check(g.Deliver(0, out reply) == null && Math.Abs(g.cash - cash - 40) < 0.01, "배달 결제 $40");
            Check(g.records.packsSold == 2, "누적 판매 기록");
        }
        {
            var g = new Game(B, 5);
            g.orders.Add(new Order { id = 1, contact = 1, product = "ground", species = "chicken", qty = 1, unitPrice = 20, created = g.T, deadline = g.T + 10, status = OrderStatus.Open });
            float rel = g.contacts[1].relationship;
            g.Advance(11);
            Check(g.orders[0].status == OrderStatus.Expired && g.contacts[1].relationship < rel, "기한 초과 시 관계 하락");
        }
        {
            // 연구: 투입 없으면 진행 없음, 투입하면 확률적으로 오른다
            var g = new Game(B, 6);
            for (int d = 0; d < 3; d++) { g.Advance(12 * 60); g.Sleep(); }
            Check(g.Level("serumFree") == 0, "예산 0이면 연구 진행 없음");
            int up = 0, trials = 40;
            for (int s = 0; s < trials; s++)
            {
                var h = new Game(B, 100 + s);
                h.SetBudget("serumFree", 400);
                for (int d = 0; d < 10; d++) { h.Advance(12 * 60); h.Sleep(); }
                if (h.Level("serumFree") > 0) up++;
            }
            Check(up > trials / 3 && up < trials, $"무혈청 배지 10일 $400/일: 레벨업 {up}/{trials}회 (확률적)");
            var k = new Game(B, 7);
            float p0 = k.MediumUnitPrice;
            k.axes["serumFree"].level = 2;
            Check(Math.Abs(k.MediumUnitPrice - p0 * 0.72f * 0.72f) < 0.01, "무혈청 Lv2 → 배지 단가 ×0.72²");
        }
        {
            // 엔젤 라운드 관문
            var g = new Game(B, 8);
            g.records.packsSold = 30;
            for (int i = 0; i < 3; i++) g.contacts[i].regular = true;
            g.Advance(60);
            var e = g.pendingEvents.FirstOrDefault(x => x.kind == "round");
            Check(e != null, "판매 30팩 + 단골 3명 → 엔젤 제안");
            float cash = g.cash;
            g.ResolveEvent(e, 0);
            Check(g.phase == 2 && g.cash > cash + 100000 && g.equity < 100, "수락 → Phase 2, 현금 증가, 지분 감소");
        }
        {
            // 면접: 질문이 추정 범위를 좁힌다
            var g = new Game(B, 9);
            g.phase = 2;
            g.Sleep();
            var c = g.candidates.FirstOrDefault();
            Check(c != null, "Phase 2 지원자 생성");
            float w0 = c.est["skill"][1] - c.est["skill"][0];
            g.ScheduleInterview(c.id, g.T + 30);
            g.Advance(31);
            Check(g.IsWaiting(c), "면접 시각에 도착");
            g.StartInterview(c.id);
            string ans;
            Check(g.Ask(c.id, "tech1", out ans) == null && ans.Length > 0, "기술 질문 답변");
            float w1 = c.est["skill"][1] - c.est["skill"][0];
            Check(w1 < w0, $"숙련도 추정 범위 축소 {w0:0.0} → {w1:0.0}");
            g.EndInterview(c.id);
            Check(g.PracticalTest(c.id) == null && c.est["skill"][0] == c.stats["skill"], "실기 테스트 → 숙련도 정확히 공개");
            bool acc;
            g.Offer(c.id, c.asking * 1.05f, out acc);
            Check(acc && g.employees.Count == 1, "요구액 이상 제안 → 입사");
        }
        {
            // 파산
            var g = new Game(B, 10);
            g.cash = 10;
            g.Advance(12 * 60);
            g.Sleep();
            Check(g.gameOver && g.gameOverReason.Contains("파산"), "현금 0 미만 → 파산");
        }
        {
            var a = new Game(B, 42); var b = new Game(B, 42);
            for (int d = 0; d < 5; d++) { a.Advance(15 * 60); a.Sleep(); b.Advance(15 * 60); b.Sleep(); }
            Check(Math.Abs(a.cash - b.cash) < 0.001 && a.orders.Count == b.orders.Count, "같은 시드 → 같은 결과");
        }
    }
}
