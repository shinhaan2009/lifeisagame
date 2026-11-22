// IMGUI 스타일: 기획서 디자인 원칙대로 흰 배경, 검은 글씨와 선만 쓴다.
using UnityEngine;

namespace LabMeat.Game
{
    public static class Ui
    {
        public static GUIStyle box, panel, label, small, title, button, buttonOn, prompt, toast, mono, bar, barFill, tab, tabOn, header;
        static bool ready;
        public static Texture2D White, Black, Grey, Light;
        public static float Scale = 1f;

        static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        // 1px 검은 테두리가 있는 흰 사각 텍스처
        static Texture2D Framed(Color fill, Color border)
        {
            var t = new Texture2D(8, 8);
            for (int x = 0; x < 8; x++)
                for (int y = 0; y < 8; y++)
                    t.SetPixel(x, y, x == 0 || y == 0 || x == 7 || y == 7 ? border : fill);
            t.Apply();
            t.filterMode = FilterMode.Point;
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        public static void Init()
        {
            Scale = Mathf.Clamp(Screen.height / 900f, 0.8f, 2f);
            if (ready) return;
            ready = true;
            White = Tex(Color.white); Black = Tex(Color.black); Grey = Tex(new Color(0.55f, 0.55f, 0.55f)); Light = Tex(new Color(0.92f, 0.92f, 0.92f));
            var font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "NanumGothic", "Arial" }, 16);
            var framed = Framed(Color.white, Color.black);
            var framedDark = Framed(Color.black, Color.black);
            var framedLight = Framed(new Color(0.9f, 0.9f, 0.9f), Color.black);

            label = new GUIStyle { font = font, fontSize = 15, wordWrap = true, richText = true };
            label.normal.textColor = Color.black;
            label.padding = new RectOffset(2, 2, 2, 2);
            small = new GUIStyle(label) { fontSize = 13 };
            small.normal.textColor = new Color(0.3f, 0.3f, 0.3f);
            mono = new GUIStyle(label) { fontSize = 14 };
            title = new GUIStyle(label) { fontSize = 20, fontStyle = FontStyle.Bold };
            header = new GUIStyle(label) { fontSize = 15, fontStyle = FontStyle.Bold };

            panel = new GUIStyle { border = new RectOffset(2, 2, 2, 2), padding = new RectOffset(16, 16, 14, 14) };
            panel.normal.background = framed;
            box = new GUIStyle(panel) { padding = new RectOffset(10, 10, 8, 8), margin = new RectOffset(0, 0, 4, 4) };

            button = new GUIStyle { font = font, fontSize = 15, alignment = TextAnchor.MiddleCenter, border = new RectOffset(2, 2, 2, 2), padding = new RectOffset(10, 10, 6, 6), margin = new RectOffset(2, 2, 2, 2), wordWrap = true };
            button.normal.background = framed; button.normal.textColor = Color.black;
            button.hover.background = framedDark; button.hover.textColor = Color.white;
            button.active.background = framedDark; button.active.textColor = Color.white;
            buttonOn = new GUIStyle(button);
            buttonOn.normal.background = framedDark; buttonOn.normal.textColor = Color.white;
            tab = new GUIStyle(button) { padding = new RectOffset(12, 12, 6, 6) };
            tabOn = new GUIStyle(buttonOn) { padding = new RectOffset(12, 12, 6, 6) };

            prompt = new GUIStyle(label) { fontSize = 17, alignment = TextAnchor.MiddleCenter };
            prompt.normal.background = framed;
            prompt.padding = new RectOffset(12, 12, 6, 6);
            toast = new GUIStyle(small) { fontSize = 14 };
            toast.normal.textColor = Color.black;
            toast.normal.background = framedLight;
            toast.padding = new RectOffset(10, 10, 5, 5);

            bar = new GUIStyle(); bar.normal.background = framed;
            barFill = new GUIStyle(); barFill.normal.background = Black;
        }

        public static void Bar(float v, float w = 160, float h = 10)
        {
            var r = GUILayoutUtility.GetRect(w, h, GUILayout.Width(w), GUILayout.Height(h));
            r.y += 4;
            GUI.Box(r, GUIContent.none, bar);
            var f = new Rect(r.x + 2, r.y + 2, (r.width - 4) * Mathf.Clamp01(v), r.height - 4);
            GUI.Box(f, GUIContent.none, barFill);
        }

        public static bool Btn(string text, bool enabled = true, params GUILayoutOption[] opts)
        {
            var old = GUI.enabled;
            GUI.enabled = enabled && old;
            bool b = GUILayout.Button(text, button, opts);
            GUI.enabled = old;
            return b;
        }

        public static int Tabs(int current, string[] names)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < names.Length; i++)
                if (GUILayout.Button(names[i], i == current ? tabOn : tab)) current = i;
            GUILayout.EndHorizontal();
            return current;
        }

        public static void L(string text) { GUILayout.Label(text, label); }
        public static void S(string text) { GUILayout.Label(text, small); }
        public static void H(string text) { GUILayout.Space(6); GUILayout.Label(text, header); }

        // GUI.matrix로 Scale만큼 확대해서 그리므로, 레이아웃은 가상 해상도(VW×VH) 기준이다.
        public static float VW { get { return Screen.width / Scale; } }
        public static float VH { get { return Screen.height / Scale; } }

        public static Rect Center(float w, float h)
        {
            w = Mathf.Min(w, VW - 40);
            h = Mathf.Min(h, VH - 40);
            return new Rect((VW - w) / 2, (VH - h) / 2, w, h);
        }

        public static string Money(float v) { return (v < 0 ? "-$" : "$") + Mathf.Abs(v).ToString("N0"); }
    }
}
