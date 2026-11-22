// 게임 진입점. 빈 씬에서 Play를 누르면 자동으로 생성되어 맵·플레이어·시뮬레이션을 만든다.
// 시뮬레이션(LabMeat.Sim)은 고정 틱(게임 1분)으로만 진행하고, 여기서는 실제 시간에 맞춰 틱을 호출한다.
using System.Linq;
using UnityEngine;
using LabMeat.Sim;
using SimGame = LabMeat.Sim.Game;

namespace LabMeat.Game
{
    public enum Panel { None, Phone, Computer, Reactor, Bench, Fridge, Door, Restaurant, Taxi, Interview, Settlement, Event, Pause, GameOver, Investor }

    public partial class GameManager : MonoBehaviour
    {
        public static GameManager I;

        public SimGame sim;
        public Balance B;
        public World world;
        public PlayerController player;
        public string zone = "home";

        public int speed = 1;
        public bool paused;
        float acc;

        public Panel panel = Panel.None;
        Interactable target;           // 열린 패널의 대상 시설
        Interactable looking;          // 지금 보고 있는 시설
        string hint = "";              // 패널 하단 피드백
        float hintTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindObjectOfType<GameManager>() != null) return;
            new GameObject("GameManager").AddComponent<GameManager>();
        }

        void Awake()
        {
            I = this;
            var text = Resources.Load<TextAsset>("balance");
            B = Json.Parse<Balance>(text.text);
            foreach (var c in FindObjectsOfType<Camera>()) Destroy(c.gameObject);
            world = new World();
            player = PlayerController.Create();
            NewGame();
        }

        void NewGame()
        {
            sim = new SimGame(B, Random.Range(1, int.MaxValue));
            zone = "home";
            player.Teleport(world.bedSpawn, world.bedYaw);
            panel = Panel.None;
            paused = false;
            speed = 1;
        }

        // 패널이 열려 있으면 이동·시점 입력을 막는다
        public bool InputLocked { get { return panel != Panel.None; } }

        // 시간이 멈추는 패널: 정산, 사건, 일시정지, 게임 오버
        bool TimeFrozen
        {
            get { return paused || panel == Panel.Settlement || panel == Panel.Event || panel == Panel.Pause || panel == Panel.GameOver || panel == Panel.Interview; }
        }

        void Update()
        {
            HandleKeys();
            if (!TimeFrozen && !sim.gameOver)
            {
                acc += Time.deltaTime * speed / Mathf.Max(0.01f, B.time.realSecondsPerGameMinute);
                int steps = Mathf.Min(240, Mathf.FloorToInt(acc));
                acc -= steps;
                for (int k = 0; k < steps; k++)
                {
                    sim.Step();
                    if (sim.MustPassOut) { DoSleep(true); break; }
                }
            }
            if (sim.gameOver && panel != Panel.GameOver) { panel = Panel.GameOver; }
            if (panel == Panel.None && sim.pendingEvents.Count > 0) panel = Panel.Event;
            world.SyncReactors(sim);
            world.SyncSun(sim.MinuteOfDay / 60f);
            SyncNpcs();
            Look();
            PumpToasts();
        }

        void HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (panel == Panel.None) panel = Panel.Pause;
                else if (panel == Panel.Interview) { }   // 면접은 '면접 종료'로만 닫는다
                else if (panel != Panel.Settlement && panel != Panel.Event && panel != Panel.GameOver) panel = Panel.None;
            }
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                if (panel == Panel.None) { panel = Panel.Phone; foreach (var m in sim.messages) m.read = true; }
                else if (panel == Panel.Phone) panel = Panel.None;
            }
            if (panel == Panel.None || panel == Panel.Phone || panel == Panel.Computer)
            {
                if (Input.GetKeyDown(KeyCode.F1)) speed = 1;
                if (Input.GetKeyDown(KeyCode.F2)) speed = 2;
                if (Input.GetKeyDown(KeyCode.F3)) speed = 4;
                if (Input.GetKeyDown(KeyCode.P)) paused = !paused;
            }
            if (panel == Panel.None && looking != null && Input.GetKeyDown(KeyCode.E)) Use(looking);
        }

        void Look()
        {
            looking = null;
            if (InputLocked) return;
            var ray = new Ray(player.cam.transform.position, player.cam.transform.forward);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 3.2f))
            {
                var i = hit.collider.GetComponentInParent<Interactable>();
                if (i != null && i.gameObject.activeInHierarchy) looking = i;
            }
        }

        void SyncNpcs()
        {
            for (int k = 0; k < world.friendNpcs.Count; k++)
            {
                int contact = sim.contacts.FindIndex(c => sim.CDef(c.def).house == k);
                bool show = contact >= 0 && sim.contacts[contact].unlocked && sim.OpenOrderFor(contact) != null && sim.Hour >= 8 && sim.Hour < 22;
                if (world.friendNpcs[k].activeSelf != show) world.friendNpcs[k].SetActive(show);
            }
            bool cand = sim.WaitingCandidates("lab").Count > 0;
            if (world.candidateNpc.activeSelf != cand) world.candidateNpc.SetActive(cand);
        }

        // ---------- 상호작용 ----------

        int ContactAtHouse(int house) { return sim.contacts.FindIndex(c => sim.CDef(c.def).house == house); }

        public string Prompt(Interactable i)
        {
            switch (i.kind)
            {
                case Kind.Reactor:
                    {
                        if (sim.phase < sim.LDef(i.location).phase) return "잠긴 구역";
                        var r = sim.ReactorAt(i.location, i.index);
                        if (r == null) return $"빈 리액터 자리 {i.index + 1} · AI 컴퓨터에서 구매";
                        return $"[E] 리액터 {i.index + 1} · {StateText(r)}";
                    }
                case Kind.Bench:
                    {
                        var j = sim.BenchAt(i.location);
                        return j != null ? $"[E] 작업대 · 가공 중 {1 - j.minutesLeft / j.totalMinutes:P0}" : $"[E] 작업대 · 수확물 {sim.lots.Count}건";
                    }
                case Kind.Fridge: return $"[E] 냉장고 · {sim.fridge.Count}팩 보관, {sim.carried.Count}/{sim.carryLimit}팩 소지";
                case Kind.Computer: return "[E] AI 컴퓨터";
                case Kind.Bed: return sim.CanSleep ? "[E] 잠자기 (하루 정산)" : $"침대 · {B.time.earliestSleepHour}:00부터 잘 수 있다";
                case Kind.Parcel:
                    {
                        int n = sim.ArrivedParcels.Count, w = sim.parcels.Count - n;
                        return n > 0 ? $"[E] 택배함 · 도착 {n}건" : w > 0 ? $"택배함 · 내일 {B.time.parcelArrivalHour}시 도착 예정 {w}건" : "택배함 · 비어 있음";
                    }
                case Kind.Door:
                    {
                        int c = ContactAtHouse(i.index);
                        if (c < 0 || !sim.contacts[c].unlocked) return "모르는 집";
                        var d = sim.CDef(c);
                        var o = sim.OpenOrderFor(c);
                        return o != null ? $"[E] {d.name}의 집 · 주문 {sim.SDef(o.species).label} {sim.PDef(o.product).label} {o.qty}팩" : $"[E] {d.name}의 집 ({d.relation})";
                    }
                case Kind.Taxi: return $"[E] 자율주행 택시 호출 · {Ui.Money(B.costs.taxiFare)}, {B.costs.taxiMinutes}분";
                case Kind.Restaurant:
                    if (sim.phase < 2) return "레스토랑 · Phase 2부터 거래";
                    return sim.contract != null && sim.contract.active ? $"[E] {sim.contract.offer.name} 납품 · 이번 주 {sim.contract.deliveredKg:0.0}/{sim.contract.offer.kgPerWeek}kg" : "레스토랑 · 계약은 AI 컴퓨터에서";
                case Kind.Investor: return "[E] 투자사 사무실";
                case Kind.Candidate:
                    {
                        var c = sim.WaitingCandidates("lab").FirstOrDefault();
                        return c != null ? $"[E] {c.name} 면접 시작" : "";
                    }
            }
            return "";
        }

        string StateText(Reactor r)
        {
            switch (r.state)
            {
                case ReactorState.Empty: return "비어 있음";
                case ReactorState.Growing: return $"{sim.SDef(r.species).label} 배양 {r.growth:P0} · 배지 {r.freshness:0}%";
                case ReactorState.Ready: return $"{sim.SDef(r.species).label} 수확 가능";
                default: return "오염 확인됨";
            }
        }

        void Use(Interactable i)
        {
            target = i;
            hint = "";
            switch (i.kind)
            {
                case Kind.Reactor:
                    if (sim.phase >= sim.LDef(i.location).phase && sim.ReactorAt(i.location, i.index) != null) panel = Panel.Reactor;
                    break;
                case Kind.Bench: panel = Panel.Bench; break;
                case Kind.Fridge: panel = Panel.Fridge; break;
                case Kind.Computer: panel = Panel.Computer; break;
                case Kind.Bed:
                    if (sim.CanSleep) DoSleep(false);
                    break;
                case Kind.Parcel:
                    {
                        var err = sim.PickUpParcels();
                        Toast(err ?? "택배를 차고 재고로 옮겼다");
                        break;
                    }
                case Kind.Door:
                    {
                        int c = ContactAtHouse(i.index);
                        lastReply = "";
                        if (c >= 0 && sim.contacts[c].unlocked) panel = Panel.Door;
                        break;
                    }
                case Kind.Taxi: panel = Panel.Taxi; break;
                case Kind.Restaurant: if (sim.phase >= 2) panel = Panel.Restaurant; break;
                case Kind.Investor: panel = Panel.Investor; break;
                case Kind.Candidate:
                    {
                        var c = sim.WaitingCandidates("lab").FirstOrDefault();
                        if (c != null)
                        {
                            if (!c.inInterview) sim.StartInterview(c.id);
                            interviewId = c.id;
                            panel = Panel.Interview;
                        }
                        break;
                    }
            }
        }

        void DoSleep(bool passedOut)
        {
            sim.Sleep(passedOut);
            zone = "home";
            player.Teleport(world.bedSpawn, world.bedYaw);
            panel = sim.gameOver ? Panel.GameOver : Panel.Settlement;
        }

        public void Travel(string to)
        {
            if (to == zone) { panel = Panel.None; return; }
            var err = sim.TakeTaxi();
            if (err != null) { SetHint(err); return; }
            zone = to;
            if (to == "home") player.Teleport(world.homeTaxi + new Vector3(1.5f, 0.1f, -1.5f), 90);
            else player.Teleport(world.downtownTaxi + new Vector3(1.5f, 0.1f, -1.5f), 90);
            panel = Panel.None;
        }

        void SetHint(string s) { hint = s; hintTime = Time.time; }

        // ---------- 토스트 ----------

        readonly System.Collections.Generic.List<(string text, float until)> toastList = new System.Collections.Generic.List<(string, float)>();

        public void Toast(string s) { toastList.Add((s, Time.time + 5f)); if (toastList.Count > 4) toastList.RemoveAt(0); }

        void PumpToasts()
        {
            while (sim.toasts.Count > 0) Toast(sim.toasts.Dequeue());
            toastList.RemoveAll(t => Time.time > t.until);
        }
    }
}
