// HUD와 패널 디스패치. 상시 HUD는 시각·현금·상호작용 프롬프트·알림 1줄만 둔다 (기획서 UI 3겹 중 1겹).
using UnityEngine;

namespace LabMeat.Game
{
    public partial class GameManager
    {
        Vector2 scroll;

        void OnGUI()
        {
            Ui.Init();
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Ui.Scale, Ui.Scale, 1));
            Hud();
            switch (panel)
            {
                case Panel.Phone: PhonePanel(); break;
                case Panel.Computer: ComputerPanel(); break;
                case Panel.Reactor: ReactorPanel(); break;
                case Panel.Bench: BenchPanel(); break;
                case Panel.Fridge: FridgePanel(); break;
                case Panel.Door: DoorPanel(); break;
                case Panel.Restaurant: RestaurantPanel(); break;
                case Panel.Taxi: TaxiPanel(); break;
                case Panel.Interview: InterviewPanel(); break;
                case Panel.Settlement: SettlementPanel(); break;
                case Panel.Event: EventPanel(); break;
                case Panel.Pause: PausePanel(); break;
                case Panel.GameOver: GameOverPanel(); break;
                case Panel.Investor: InvestorPanel(); break;
            }
        }

        void Hud()
        {
            float W = Ui.VW, H = Ui.VH;
            string spd = paused ? "일시정지" : $"×{speed}";
            string top = $"{sim.day}일차  {sim.ClockText}  {spd}    {Ui.Money(sim.cash)}    여론 {sim.opinion:0}    Phase {sim.phase}";
            GUI.Label(new Rect(12, 10, 620, 30), top, Ui.prompt);
            int unread = sim.UnreadCount;
            string sub = $"소지 {sim.carried.Count}/{sim.carryLimit}팩" + (unread > 0 ? $"   휴대 단말 새 메시지 {unread} (Tab)" : "   Tab 휴대 단말");
            GUI.Label(new Rect(12, 44, 620, 24), sub, Ui.small);

            // 알림 1줄 (최근 것부터 최대 3개)
            float y = 10;
            for (int k = toastList.Count - 1; k >= 0 && k >= toastList.Count - 3; k--)
            {
                var c = new GUIContent(toastList[k].text);
                float w = Mathf.Min(560, Ui.toast.CalcSize(c).x + 4);
                float h = Ui.toast.CalcHeight(c, w);
                GUI.Label(new Rect(W - w - 12, y, w, h), c, Ui.toast);
                y += h + 4;
            }

            if (panel != Panel.None) return;
            // 조준점
            GUI.DrawTexture(new Rect(W / 2 - 2, H / 2 - 2, 4, 4), Ui.Black);
            if (looking != null)
            {
                string p = Prompt(looking);
                if (!string.IsNullOrEmpty(p))
                {
                    var c = new GUIContent(p);
                    float w = Ui.prompt.CalcSize(c).x + 8;
                    GUI.Label(new Rect(W / 2 - w / 2, H * 0.62f, w, 34), c, Ui.prompt);
                }
            }
            GUI.Label(new Rect(12, H - 30, 900, 24), "WASD 이동 · Shift 달리기 · E 상호작용 · Tab 휴대 단말 · F1/F2/F3 배속 1×/2×/4× · P 일시정지 · Esc 메뉴", Ui.small);
            WorldLabels();
        }

        // 가까운 시설 위에 이름표
        void WorldLabels()
        {
            var cam = player.cam;
            foreach (var i in world.stations)
            {
                if (i == null || !i.gameObject.activeInHierarchy) continue;
                if (i.kind == Kind.Door || i.kind == Kind.Candidate) continue;
                var p = i.transform.position + Vector3.up * (i.kind == Kind.Reactor ? 2.3f : 1.4f);
                float d = Vector3.Distance(cam.transform.position, p);
                if (d > 7f) continue;
                var sp = cam.WorldToScreenPoint(p);
                if (sp.z <= 0) continue;
                float x = sp.x / Ui.Scale, yy = (Screen.height - sp.y) / Ui.Scale;
                string text = i.label;
                if (i.kind == Kind.Reactor)
                {
                    var r = sim.ReactorAt(i.location, i.index);
                    text = r == null ? "빈 자리" : StateText(r);
                }
                var c = new GUIContent(text);
                float w = Ui.small.CalcSize(c).x;
                GUI.Label(new Rect(x - w / 2, yy - 10, w + 4, 22), c, Ui.small);
            }
        }

        // 패널 공통 틀
        void Begin(string title, float w, float h)
        {
            var r = Ui.Center(w, h);
            GUILayout.BeginArea(r, Ui.panel);
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, Ui.title);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{sim.day}일차 {sim.ClockText}  {Ui.Money(sim.cash)}", Ui.small, GUILayout.Width(240));
            GUILayout.EndHorizontal();
        }

        void End(bool closable = true)
        {
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(hint) && Time.time - hintTime < 6f) GUILayout.Label(hint, Ui.header);
            if (closable && Ui.Btn("닫기 (Esc)", true, GUILayout.Width(140))) panel = Panel.None;
            GUILayout.EndArea();
        }

        void Result(string err, string ok)
        {
            SetHint(err ?? ok);
        }
    }
}
