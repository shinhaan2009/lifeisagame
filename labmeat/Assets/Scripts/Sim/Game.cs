// 게임 코어: 시간, 현금, 하루 정산. 기능별 규칙은 Game.*.cs partial 파일에 있다.
// 고정 틱 = 게임 내 1분. Unity 층은 실제 시간에 맞춰 Step()을 호출만 한다.
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabMeat.Sim
{
    public partial class Game
    {
        public readonly Balance B;
        public readonly Rng rng;

        public long T;                 // 절대 게임 분
        public int day = 1;            // 아침 기준 날짜
        public float cash;
        public float equity = 100f;    // 창업자 지분 %
        public float opinion;
        public int phase = 1;
        public bool gameOver; public string gameOverReason = "";
        public int carryLimit;

        public Dictionary<string, int> stock = new Dictionary<string, int>();
        public List<Reactor> reactors = new List<Reactor>();
        public List<RawLot> lots = new List<RawLot>();
        public List<Pack> fridge = new List<Pack>();
        public List<Pack> carried = new List<Pack>();
        public List<BenchJob> jobs = new List<BenchJob>();
        public List<Parcel> parcels = new List<Parcel>();

        public List<Message> messages = new List<Message>();
        public Queue<string> toasts = new Queue<string>();
        public List<GameEvent> pendingEvents = new List<GameEvent>();
        public Ledger ledger = new Ledger();
        public Settlement lastSettlement;
        public Records records = new Records();

        int nextId = 1;
        public int NewId() { return nextId++; }

        public Game(Balance balance, int seed = 1)
        {
            B = balance;
            rng = new Rng(seed);
            T = B.time.wakeHour * 60;
            cash = B.start.cash;
            opinion = B.start.opinion;
            carryLimit = B.start.carryLimit;
            foreach (var m in B.materials) stock[m.id] = 0;
            foreach (var kv in B.start.materials) stock[kv.Key] = kv.Value;
            AddReactor(B.start.startReactor, "garage");
            InitResearch();
            InitSales();
            InitCompany();
            InitHiring();
            Notify("AI", "차고 연구실 가동 준비 완료. 리액터 1기, 닭 세포 바이알과 배지가 있습니다. 먼저 리액터에 세포를 접종하세요.");
        }

        // ---------- 시간 ----------

        public long DayStart { get { return (long)(day - 1) * 1440; } }
        public int MinuteOfDay { get { return (int)(T - DayStart); } }
        public int Hour { get { return MinuteOfDay / 60; } }
        public string ClockText
        {
            get { int m = MinuteOfDay; return $"{(m / 60) % 24:00}:{m % 60:00}"; }
        }
        public bool CanSleep { get { return Hour >= B.time.earliestSleepHour; } }
        public bool MustPassOut { get { return MinuteOfDay >= B.time.forcedSleepHour * 60; } }
        public string Fmt(long minute)
        {
            long d = minute / 1440 + 1; long m = minute % 1440;
            return $"{d}일 {m / 60:00}:{m % 60:00}";
        }

        // 1분 진행
        public void Step()
        {
            if (gameOver) return;
            T++;
            StepProduction();
            StepSales();
            StepCompany();
            StepHiring();
            if (T % 60 == 0) HourlyEmployees();
        }

        public void Advance(int minutes)
        {
            for (int k = 0; k < minutes && !gameOver; k++) Step();
        }

        // 잠들기: 다음 날 기상까지 시뮬레이션을 돌리고 정산한다.
        public Settlement Sleep(bool passedOut = false)
        {
            if (gameOver) return null;
            if (passedOut)
            {
                ledger.notes.Add("새벽 2시에 기절했다. 다음 날 컨디션이 나쁘다.");
            }
            long wakeT = (long)day * 1440 + B.time.wakeHour * 60 + (passedOut ? 120 : 0);
            while (T < wakeT && !gameOver) Step();
            var s = Settle();
            day++;
            MorningEmployees();
            return s;
        }

        Settlement Settle()
        {
            // 고정비
            Spend("생활비", B.costs.livingPerDay);
            int running = reactors.Count;
            Spend("에너지", running * B.costs.energyPerReactorPerDay);
            var tier = B.compute[computeTier];
            Spend("연산 장비", tier.perDay);
            foreach (var loc in B.locations)
                if (phase >= loc.phase && loc.rentPerDay > 0) Spend(loc.label + " 임대", loc.rentPerDay);
            PaySalaries();
            ResolveResearch();
            DailyCompany();
            DailySales();
            DailyHiring();

            var s = new Settlement
            {
                day = day, revenue = ledger.revenue, costs = new Dictionary<string, float>(ledger.costs),
                cash = cash, notes = new List<string>(ledger.notes)
            };
            lastSettlement = s;
            ledger = new Ledger();
            if (cash < 0) EndGame("파산: 현금이 바닥났다");
            else if (opinion <= 0) EndGame("파산: 여론 0, 모든 시장에서 판매 금지");
            return s;
        }

        public void EndGame(string reason)
        {
            gameOver = true;
            gameOverReason = reason;
            Notify("시스템", reason);
        }

        // ---------- 돈 ----------

        public bool CanAfford(float amount) { return cash >= amount; }

        public void Spend(string category, float amount)
        {
            if (amount <= 0) return;
            cash -= amount;
            ledger.Cost(category, amount);
        }

        public void Earn(float amount, string why)
        {
            cash += amount;
            ledger.revenue += amount;
            records.revenue += amount;
        }

        // ---------- 알림 ----------

        public void Notify(string from, string text)
        {
            messages.Add(new Message { minute = T, from = from, text = text });
            if (messages.Count > 200) messages.RemoveAt(0);
            toasts.Enqueue($"[{from}] {text}");
            while (toasts.Count > 6) toasts.Dequeue();
        }

        public void Raise(GameEvent e) { pendingEvents.Add(e); }

        public int UnreadCount { get { return messages.Count(m => !m.read); } }

        public MaterialDef Mat(string id) { return B.materials.First(m => m.id == id); }
        public ReactorDef RDef(string id) { return B.reactors.First(r => r.id == id); }
        public SpeciesDef SDef(string id) { return B.species.First(r => r.id == id); }
        public ProductDef PDef(string id) { return B.products.First(r => r.id == id); }
        public AxisDef ADef(string id) { return B.research.First(r => r.id == id); }
        public LocationDef LDef(string id) { return B.locations.First(r => r.id == id); }
    }
}
