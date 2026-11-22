// 게임 상태 자료형. 모두 순수 데이터.
using System;
using System.Collections.Generic;

namespace LabMeat.Sim
{
    public class Rng
    {
        ulong s;
        public Rng(int seed) { s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL; if (s == 0) s = 1; }
        public double Next()
        {
            s ^= s << 13; s ^= s >> 7; s ^= s << 17;
            return (s >> 11) * (1.0 / 9007199254740992.0);
        }
        public float Range(float a, float b) { return a + (float)Next() * (b - a); }
        public int Int(int a, int bExclusive) { return a + (int)(Next() * (bExclusive - a)); }
        public bool Chance(double p) { return Next() < p; }
        public float Normal(float mean, float sd)
        {
            double u1 = 1.0 - Next(), u2 = Next();
            return mean + sd * (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
        }
        public T Pick<T>(IList<T> l) { return l[Int(0, l.Count)]; }
    }

    public struct Quality
    {
        public float taste, texture, safety;
        public float Score(float textureWeight)
        {
            float tw = textureWeight, rest = (1f - tw);
            return taste * rest * 0.6f + texture * tw + safety * rest * 0.4f;
        }
        public override string ToString() { return $"맛 {taste:0} · 질감 {texture:0} · 안전 {safety:0}"; }
    }

    public enum ReactorState { Empty, Growing, Ready, Contaminated }

    public class Reactor
    {
        public int id; public string def; public string location; public int slot;
        public ReactorState state;
        public string species;
        public long startMinute;
        public float growth;            // 0..1
        public float freshness;         // 배지 신선도 0..100
        public float freshSum; public int freshSamples;
        public bool contaminated;       // 숨김
        public long contamMinute;
        public float overripeHours;
        public float batchCost;
        public long boughtDay;
        public string lastCheck = "";
    }

    public class RawLot
    {
        public int id; public string species; public float kg; public Quality q; public bool contaminatedHidden; public float cost; public int day; public string location;
    }

    public class Pack
    {
        public int id; public string product; public string species; public Quality q; public bool contaminatedHidden; public float cost;
        public long madeMinute; public long takenMinute = -1;
    }

    public class BenchJob
    {
        public string location; public int lotId; public string product; public int packs; public float minutesLeft; public float totalMinutes;
        public string species; public Quality q; public bool contam; public float costPerPack;
    }

    public class Parcel
    {
        public long arrivalMinute; public Dictionary<string, int> items = new Dictionary<string, int>(); public bool notified;
    }

    public enum Outcome { None, Fail, Partial, Breakthrough }

    public class AxisState
    {
        public string id;
        public int level; public float progress;   // progress 0..1 → 다음 레벨
        public float points;                       // 이번 실험에 누적된 투입
        public float budget;                       // 일일 예산
        public float hiddenMult;                   // 이번 판에서의 숨은 난이도 배수
        public Outcome lastOutcome; public string lastNote = "";
        public float aiEstimate;                   // AI가 본 다음 날 성공 확률 추정
    }

    public class ContactState
    {
        public int def; public bool unlocked; public float relationship; public int deliveries; public int failures; public bool regular;
        public long nextOrderAllowed;
    }

    public enum OrderStatus { Open, Delivered, Expired, Declined }

    public class Order
    {
        public int id; public int contact; public string product; public string species; public int qty; public float unitPrice;
        public long created; public long deadline; public OrderStatus status;
    }

    public class Contract
    {
        public RestaurantOffer offer; public int weekStartDay; public float deliveredKg; public int weeksDone; public int weeksFulfilled; public bool active;
    }

    public enum ApprovalState { None, Reviewing, Approved }

    public class Employee
    {
        public int id; public string name;
        public Dictionary<string, int> stats = new Dictionary<string, int>();     // 실제 값 (숨김)
        public Dictionary<string, float[]> est = new Dictionary<string, float[]>(); // 추정 범위 [lo, hi]
        public List<string> traits = new List<string>(); public List<string> knownTraits = new List<string>();
        public float salary; public string role = "culture"; public string axis = "serumFree"; public string location = "lab";
        public int daysWorked; public bool absentToday; public bool raiseAsked;
        public Dictionary<string, int> revealDay = new Dictionary<string, int>();
    }

    public class Candidate
    {
        public int id; public string name;
        public Dictionary<string, int> stats = new Dictionary<string, int>();
        public Dictionary<string, int> claims = new Dictionary<string, int>();
        public Dictionary<string, float[]> est = new Dictionary<string, float[]>();
        public List<string> traits = new List<string>();
        public float presentation;     // 긴장(-) / 말솜씨(+)
        public float asking;
        public int offerDeadlineDay;
        public long interviewMinute = -1; public string interviewLocation = "lab";
        public bool interviewed; public bool inInterview; public int minutesUsed;
        public List<string> asked = new List<string>();
        public List<string> transcript = new List<string>();
        public bool practicalDone; public bool referencePending; public bool referenceDone; public int referenceReadyDay;
        public bool careerChecked; public bool gone;
        public string aiNote = "";
    }

    public class GameEvent
    {
        public string kind; public string title; public string text; public string[] options; public int data; public float amount;
    }

    public class Message
    {
        public long minute; public string from; public string text; public bool read;
    }

    public class NewsItem
    {
        public long minute; public string kind; public string headline;
    }

    public class Buff
    {
        public string kind; public string axis; public float mult; public int untilDay;
    }

    public class Ledger
    {
        public float revenue;
        public Dictionary<string, float> costs = new Dictionary<string, float>();
        public List<string> notes = new List<string>();
        public void Cost(string k, float v) { float o; costs.TryGetValue(k, out o); costs[k] = o + v; }
        public float TotalCost() { float t = 0; foreach (var v in costs.Values) t += v; return t; }
    }

    public class Settlement
    {
        public int day; public float revenue; public Dictionary<string, float> costs; public float cash; public List<string> notes;
    }

    public class Records
    {
        public float revenue; public float lowestCostPerKg = float.MaxValue; public int countries; public int recalls;
        public int packsSold; public List<bool> batches = new List<bool>();
        public float BatchSuccess(int lastN)
        {
            int n = Math.Min(lastN, batches.Count);
            if (n == 0) return 0;
            int ok = 0;
            for (int k = batches.Count - n; k < batches.Count; k++) if (batches[k]) ok++;
            return ok / (float)n;
        }
    }
}
