// 단순한 스크립트 플레이어. 밸런스 확인용: 매일 아침 리액터 관리, 가공, 배달, 저녁 연구 예산, 밤 수면.
using System;
using System.Collections.Generic;
using System.Linq;
using LabMeat.Sim;

static class Bot
{
    public static void Run(Balance B, bool verbose)
    {
        var results = new List<string>();
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 6 })
        {
            var g = new Game(B, seed);
            int angelDay = -1, seriesADay = -1;
            for (int d = 0; d < 120 && !g.gameOver; d++)
            {
                Morning(g);
                for (int h = 0; h < 13 && !g.gameOver; h++)
                {
                    g.Advance(60);
                    Hourly(g);
                }
                Evening(g);
                foreach (var e in g.pendingEvents.ToList()) g.ResolveEvent(e, e.kind == "lobby" && g.cash < 30000 ? 1 : 0);
                if (g.phase >= 2 && angelDay < 0) angelDay = g.day;
                if (g.phase >= 3 && seriesADay < 0) seriesADay = g.day;
                var s = g.Sleep();
                if (verbose && (g.day % 5 == 0 || g.gameOver))
                    Console.WriteLine($"seed {seed} day {g.day,3} P{g.phase} cash {g.cash,10:N0} op {g.opinion,4:0} sold {g.records.packsSold,4} reg {g.RegularCount} react {g.reactors.Count} sf{g.Level("serumFree")} cl{g.Level("cellLine")} st{g.Level("sterility")} $/kg {(g.records.lowestCostPerKg < 1e9 ? g.records.lowestCostPerKg : 0),6:0} emp {g.employees.Count} appr {g.approval} rw {g.TotalRestaurantWeeks} bs {g.records.BatchSuccess(10):P0}");
            }
            results.Add($"seed {seed}: {(g.gameOver ? g.gameOverReason + " @day " + g.day : "생존")} | 엔젤 {angelDay}일 | 시리즈A {seriesADay}일 | 현금 {g.cash:N0} | 판매 {g.records.packsSold}팩 | 최저 원가 ${(g.records.lowestCostPerKg < 1e9 ? g.records.lowestCostPerKg : 0):0}/kg | 리콜 {g.records.recalls}");
        }
        Console.WriteLine(string.Join("\n", results));
    }

    static void Morning(Game g)
    {
        g.PickUpParcels();
        foreach (var r in g.reactors.ToList()) Tend(g, r);
        foreach (var lot in g.lots.ToList())
        {
            string loc = lot.location;
            if (g.BenchAt(loc) == null) g.StartBench(loc, lot.id, g.ProductUnlocked("nugget") && g.rng.Chance(0.4) ? "nugget" : "ground");
        }
        // 재고 유지
        var need = new Dictionary<string, int>();
        int L = g.reactors.Sum(r => g.RDef(r.def).capacityL);
        if (g.stock["medium"] < L * 2) need["medium"] = L * 3;
        if (g.stock["vial"] < g.reactors.Count) need["vial"] = g.reactors.Count * 2;
        if (g.stock["packaging"] < 40) need["packaging"] = 60;
        if (need.Count > 0 && g.cash > g.OrderCost(need) + 2000) g.OrderMaterials(need);
        // 확장
        if (g.phase == 1 && g.cash > 14000 && g.reactors.Count(r => r.location == "garage") < 3) g.BuyReactor("bench", "garage");
        if (g.phase >= 2)
        {
            if (g.approval == ApprovalState.None && g.cash > 60000) g.ApplyApproval();
            if ((g.contract == null || !g.contract.active) && g.TotalRestaurantWeeks < 3) g.AcceptContract(0);
            if (g.reactors.Count(r => r.def == "pilot") < 1 && g.cash > 60000) g.BuyReactor("pilot", "lab");
            if (g.employees.Count < 2)
            {
                var c = g.candidates.FirstOrDefault(x => x.interviewMinute < 0);
                if (c != null) g.ScheduleInterview(c.id, g.T + 60);
            }
        }
    }

    static void Tend(Game g, Reactor r)
    {
        if (r.state == ReactorState.Contaminated) { g.Discard(r); }
        if (r.state == ReactorState.Ready) { string e; g.Harvest(r, out e); }
        if (r.state == ReactorState.Growing)
        {
            g.Check(r);
            if (r.lastCheck.Contains("탁하다")) { g.Discard(r); return; }
            if (r.freshness < 70) g.ChangeMedium(r);
        }
        if (r.state == ReactorState.Empty) g.Inoculate(r, g.SpeciesUnlocked("pork") ? "pork" : "chicken");
    }

    static void Hourly(Game g)
    {
        // 면접 대기자 처리
        foreach (var c in g.WaitingCandidates("lab").ToList())
        {
            if (!c.inInterview) g.StartInterview(c.id);
            string a;
            foreach (var q in new[] { "tech1", "situ1", "tech3", "career1" }) g.Ask(c.id, q, out a);
            g.EndInterview(c.id);
            bool acc; g.Offer(c.id, c.asking, out acc);
            if (acc) { var e = g.employees.Last(); g.SetRole(e.id, g.employees.Count == 1 ? "culture" : "processing"); }
        }
        // 배달: 냉장고에서 꺼내 바로 전달 (이동 20분)
        foreach (var o in g.OpenOrders.ToList())
        {
            var packs = g.fridge.Where(p => p.product == o.product && p.species == o.species).Take(o.qty).ToList();
            if (packs.Count < o.qty) continue;
            foreach (var p in packs) g.TakeFromFridge(p.id);
            g.Advance(20);
            string reply; g.Deliver(o.contact, out reply);
        }
        if (g.contract != null && g.contract.active && g.contract.deliveredKg < g.contract.offer.kgPerWeek && g.fridge.Count > 4 && g.Hour == 15)
        {
            foreach (var p in g.fridge.Take(8).ToList()) g.TakeFromFridge(p.id);
            g.TakeTaxi(); float kg; g.DeliverRestaurant(out kg);
            foreach (var p in g.carried.ToList()) g.PutInFridge(p.id);
        }
        if (g.approval == ApprovalState.Approved && g.Hour == 17 && g.fridge.Count > 10) g.Export(g.fridge.Count - 10);
        if (g.Hour == 12) foreach (var r in g.reactors.ToList()) if (r.state == ReactorState.Ready) Tend(g, r);
    }

    static void Evening(Game g)
    {
        foreach (var r in g.reactors.ToList()) Tend(g, r);
        float reserve = g.phase == 1 ? 8000 : 40000;
        float budget = Math.Max(0, Math.Min(g.phase == 1 ? 300 : 1500, (g.cash - reserve) / 40f));
        g.SetBudget("serumFree", budget * 0.6f);
        g.SetBudget("cellLine", budget * 0.25f);
        g.SetBudget("sterility", budget * 0.15f);
        if (g.computeTier == 0 && g.cash > 20000) g.BuyCompute(1);
    }
}
