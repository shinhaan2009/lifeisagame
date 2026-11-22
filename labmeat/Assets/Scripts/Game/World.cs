// 그레이박스 맵을 코드로 생성한다. 씬 파일이나 프리팹 없이 Play만 누르면 동작한다.
// 구역 2개: 주택가(Phase 1, 원점), 도심(Phase 2, z+600). 구역 사이는 자율주행 택시로 이동한다.
using System.Collections.Generic;
using UnityEngine;

namespace LabMeat.Game
{
    public class World
    {
        public const float DowntownZ = 600f;
        public readonly Transform root;
        public Vector3 bedSpawn, homeTaxi, downtownTaxi;
        public float bedYaw;
        public readonly Dictionary<string, Transform[]> reactorSlots = new Dictionary<string, Transform[]>();
        public readonly List<GameObject> friendNpcs = new List<GameObject>();
        public GameObject candidateNpc;
        public readonly List<Interactable> stations = new List<Interactable>();
        public Light sun;

        static Material matWhite, matLight, matMid, matDark, matBlack, matGround, matStreet;

        public World()
        {
            root = new GameObject("World").transform;
            matWhite = Mat(new Color(0.96f, 0.96f, 0.96f));
            matLight = Mat(new Color(0.82f, 0.82f, 0.82f));
            matMid = Mat(new Color(0.6f, 0.6f, 0.6f));
            matDark = Mat(new Color(0.3f, 0.3f, 0.3f));
            matBlack = Mat(new Color(0.08f, 0.08f, 0.08f));
            matGround = Mat(new Color(0.88f, 0.88f, 0.88f));
            matStreet = Mat(new Color(0.7f, 0.7f, 0.7f));
            Lighting();
            BuildHome();
            BuildDowntown();
        }

        public static Material Mat(Color c)
        {
            var sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Diffuse");
            var m = new Material(sh) { color = c };
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.1f);
            return m;
        }

        public static Material Shade(float grey) { return Mat(new Color(grey, grey, grey)); }

        void Lighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f);
            foreach (var l in Object.FindObjectsOfType<Light>()) if (l.type == LightType.Directional) sun = l;
            if (sun == null)
            {
                var go = new GameObject("Sun");
                sun = go.AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.color = Color.white;
            sun.shadows = LightShadows.Soft;
            if (Camera.main != null) Camera.main.backgroundColor = Color.white;
        }

        // ---------- 기본 도형 ----------

        GameObject Box(string name, Vector3 center, Vector3 size, Material m, Transform parent = null, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent ?? root, false);
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) Object.Destroy(go.GetComponent<Collider>());
            return go;
        }

        GameObject Prim(PrimitiveType t, string name, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        Interactable Tag(GameObject go, Kind k, string location, int index, string label)
        {
            var i = go.AddComponent<Interactable>();
            i.kind = k; i.location = location; i.index = index; i.label = label;
            stations.Add(i);
            return i;
        }

        // 남쪽(-z) 또는 북쪽(+z) 벽에 문이 있는 방. 바닥은 지면을 쓴다.
        void Room(string name, Vector3 c, float w, float d, bool doorSouth, float doorW = 1.8f, float h = 3f)
        {
            float t = 0.2f;
            var p = new GameObject(name).transform; p.SetParent(root, false);
            Box(name + " 벽 동", c + new Vector3(w / 2, h / 2, 0), new Vector3(t, h, d), matWhite, p);
            Box(name + " 벽 서", c + new Vector3(-w / 2, h / 2, 0), new Vector3(t, h, d), matWhite, p);
            float doorZ = doorSouth ? -d / 2 : d / 2, solidZ = -doorZ;
            Box(name + " 벽", c + new Vector3(0, h / 2, solidZ), new Vector3(w, h, t), matWhite, p);
            float seg = (w - doorW) / 2;
            Box(name + " 문옆1", c + new Vector3(-w / 2 + seg / 2, h / 2, doorZ), new Vector3(seg, h, t), matWhite, p);
            Box(name + " 문옆2", c + new Vector3(w / 2 - seg / 2, h / 2, doorZ), new Vector3(seg, h, t), matWhite, p);
            Box(name + " 문위", c + new Vector3(0, h - 0.3f, doorZ), new Vector3(doorW, 0.6f, t), matWhite, p);
            Box(name + " 지붕", c + new Vector3(0, h + 0.1f, 0), new Vector3(w + 0.4f, 0.2f, d + 0.4f), matLight, p);
            Box(name + " 바닥", c + new Vector3(0, 0.01f, 0), new Vector3(w, 0.02f, d), matWhite, p, false);
            var lg = new GameObject(name + " 조명"); lg.transform.SetParent(p, false);
            lg.transform.position = c + new Vector3(0, h - 0.4f, 0);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = Mathf.Max(w, d) * 1.2f; l.intensity = 1.2f;
        }

        void Ground(Vector3 c, float w, float d)
        {
            Box("지면", c + new Vector3(0, -0.5f, 0), new Vector3(w, 1f, d), matGround);
            Box("도로", c + new Vector3(0, 0.005f, 0), new Vector3(w, 0.01f, 7f), matStreet, null, false);
            // 경계
            float hgt = 4f;
            Box("경계", c + new Vector3(0, hgt / 2, d / 2), new Vector3(w, hgt, 0.5f), matLight);
            Box("경계", c + new Vector3(0, hgt / 2, -d / 2), new Vector3(w, hgt, 0.5f), matLight);
            Box("경계", c + new Vector3(w / 2, hgt / 2, 0), new Vector3(0.5f, hgt, d), matLight);
            Box("경계", c + new Vector3(-w / 2, hgt / 2, 0), new Vector3(0.5f, hgt, d), matLight);
        }

        Transform[] ReactorRow(string location, Vector3[] spots)
        {
            var slots = new Transform[spots.Length];
            for (int k = 0; k < spots.Length; k++)
            {
                var pad = Box($"{location} 리액터 자리 {k + 1}", spots[k] + new Vector3(0, 0.02f, 0), new Vector3(1.3f, 0.04f, 1.3f), matMid);
                Tag(pad, Kind.Reactor, location, k, $"리액터 {k + 1}");
                var bc = pad.GetComponent<BoxCollider>();
                bc.size = new Vector3(1f, 60f, 1f);      // 위에 놓일 본체까지 상호작용 범위 (월드 높이 약 2.4m)
                bc.center = new Vector3(0, 28f, 0);
                slots[k] = pad.transform;
            }
            reactorSlots[location] = slots;
            return slots;
        }

        GameObject Npc(string name, Vector3 pos, float yaw)
        {
            var body = Prim(PrimitiveType.Capsule, name, pos + new Vector3(0, 1f, 0), new Vector3(0.6f, 0.9f, 0.6f), matDark);
            var head = Prim(PrimitiveType.Sphere, name + " 머리", pos + new Vector3(0, 2.05f, 0), Vector3.one * 0.38f, matMid);
            head.transform.SetParent(body.transform, true);
            Object.Destroy(head.GetComponent<Collider>());
            var nose = Prim(PrimitiveType.Cube, name + " 시선", pos + new Vector3(0, 2.05f, 0) + Quaternion.Euler(0, yaw, 0) * Vector3.forward * 0.2f, Vector3.one * 0.1f, matBlack);
            nose.transform.SetParent(body.transform, true);
            Object.Destroy(nose.GetComponent<Collider>());
            body.transform.rotation = Quaternion.Euler(0, yaw, 0);
            return body;
        }

        // ---------- 주택가 ----------

        static readonly Vector2[] FriendHouses =
        {
            new Vector2(-30, -1), new Vector2(-15, -1), new Vector2(15, -1), new Vector2(30, -1),
            new Vector2(45, -1), new Vector2(-45, -1), new Vector2(-32, 1), new Vector2(38, 1),
        };

        void BuildHome()
        {
            Ground(Vector3.zero, 120, 70);

            // 집
            Room("집", new Vector3(0, 0, 12), 10, 8, true);
            var bed = Box("침대", new Vector3(-3, 0.3f, 14.4f), new Vector3(2.2f, 0.6f, 1.5f), matLight);
            Box("베개", new Vector3(-3.8f, 0.68f, 14.4f), new Vector3(0.5f, 0.16f, 1.1f), matWhite, null, false);
            Tag(bed, Kind.Bed, "home", 0, "침대");
            bedSpawn = new Vector3(-1.2f, 0.1f, 13f); bedYaw = 180f;
            Box("주방 조리대", new Vector3(3.2f, 0.45f, 15.4f), new Vector3(3f, 0.9f, 0.8f), matMid);

            // 차고
            Room("차고", new Vector3(14, 0, 12), 10, 8, true, 2.6f);
            ReactorRow("garage", new[] { new Vector3(11, 0, 14.8f), new Vector3(14, 0, 14.8f), new Vector3(17, 0, 14.8f) });
            Desk("garage", new Vector3(18.2f, 0, 10.2f), -90);
            Bench("garage", new Vector3(10.1f, 0, 10.5f), 90);
            Fridge("garage", new Vector3(10.0f, 0, 13.0f), 90);

            var parcel = Box("택배함", new Vector3(4, 0.4f, 6.6f), new Vector3(1.2f, 0.8f, 0.8f), matMid);
            Tag(parcel, Kind.Parcel, "home", 0, "택배함");

            homeTaxi = new Vector3(-14, 0, 4.6f);
            TaxiStand(homeTaxi, "home");

            for (int k = 0; k < FriendHouses.Length; k++)
            {
                var v = FriendHouses[k];
                bool south = v.y < 0;
                float x = v.x, zc = south ? -14f : 16f, doorZ = south ? -10.9f : 12.9f;
                Box($"지인 집 {k + 1}", new Vector3(x, 2.5f, zc), new Vector3(8, 5, 6), matWhite);
                Box($"지인 집 {k + 1} 지붕", new Vector3(x, 5.2f, zc), new Vector3(8.6f, 0.4f, 6.6f), matLight);
                var door = Box($"지인 집 {k + 1} 문", new Vector3(x, 1.1f, doorZ), new Vector3(1.2f, 2.2f, 0.25f), matDark);
                Tag(door, Kind.Door, "home", k, $"집 {k + 1}");
                var npc = Npc($"지인 {k + 1}", new Vector3(x + 1.4f, 0, south ? -9.8f : 11.8f), south ? 0 : 180);
                npc.SetActive(false);
                friendNpcs.Add(npc);
            }
        }

        void BuildDowntown()
        {
            var o = new Vector3(0, 0, DowntownZ);
            Ground(o, 120, 70);
            Room("공유 실험실", o + new Vector3(0, 0, 13), 16, 10, true, 2.6f);
            ReactorRow("lab", new[] { o + new Vector3(-6, 0, 16.2f), o + new Vector3(-2, 0, 16.2f), o + new Vector3(2, 0, 16.2f), o + new Vector3(6, 0, 16.2f) });
            Desk("lab", o + new Vector3(7.2f, 0, 10.2f), -90);
            Bench("lab", o + new Vector3(-7.1f, 0, 10.5f), 90);
            Fridge("lab", o + new Vector3(-7.0f, 0, 13.2f), 90);
            candidateNpc = Npc("면접 지원자", o + new Vector3(1.5f, 0, 9.4f), 180);
            Tag(candidateNpc, Kind.Candidate, "lab", 0, "지원자");
            candidateNpc.SetActive(false);

            Box("레스토랑 거리", o + new Vector3(26, 3, -14), new Vector3(18, 6, 8), matWhite);
            var rest = Box("레스토랑 문", o + new Vector3(26, 1.1f, -9.9f), new Vector3(1.6f, 2.2f, 0.25f), matDark);
            Tag(rest, Kind.Restaurant, "downtown", 0, "레스토랑 납품구");
            Box("투자사 사무실", o + new Vector3(-26, 5, -14), new Vector3(14, 10, 8), matLight);
            var inv = Box("투자사 사무실 문", o + new Vector3(-26, 1.1f, -9.9f), new Vector3(1.6f, 2.2f, 0.25f), matDark);
            Tag(inv, Kind.Investor, "downtown", 0, "투자사 사무실");

            downtownTaxi = o + new Vector3(-14, 0, 4.6f);
            TaxiStand(downtownTaxi, "downtown");
        }

        void Desk(string location, Vector3 p, float yaw)
        {
            var q = Quaternion.Euler(0, yaw, 0);
            Box("책상", p + new Vector3(0, 0.38f, 0), new Vector3(1.6f, 0.76f, 0.8f), matLight).transform.rotation = q;
            var mon = Box("AI 컴퓨터", p + new Vector3(0, 1.05f, 0) + q * new Vector3(0, 0, 0.15f), new Vector3(0.9f, 0.55f, 0.06f), matBlack);
            mon.transform.rotation = q;
            var desk = mon;
            Tag(desk, Kind.Computer, location, 0, "AI 컴퓨터");
            desk.GetComponent<BoxCollider>().size = new Vector3(2f, 3f, 12f);
        }

        void Bench(string location, Vector3 p, float yaw)
        {
            var b = Box("작업대", p + new Vector3(0, 0.45f, 0), new Vector3(0.9f, 0.9f, 1.8f), matMid);
            Tag(b, Kind.Bench, location, 0, "작업대");
        }

        void Fridge(string location, Vector3 p, float yaw)
        {
            var f = Box("냉장고", p + new Vector3(0, 1f, 0), new Vector3(0.8f, 2f, 0.8f), matWhite);
            Box("냉장고 손잡이", p + new Vector3(0.42f, 1.1f, 0.25f), new Vector3(0.04f, 0.6f, 0.05f), matBlack, null, false);
            Tag(f, Kind.Fridge, location, 0, "냉장고");
        }

        void TaxiStand(Vector3 p, string zone)
        {
            Box("택시 정류장 기둥", p + new Vector3(0, 1.2f, 0), new Vector3(0.12f, 2.4f, 0.12f), matDark);
            var sign = Box("택시 호출기", p + new Vector3(0, 2.3f, 0), new Vector3(0.9f, 0.5f, 0.08f), matBlack);
            sign.GetComponent<BoxCollider>().size = new Vector3(2.5f, 6f, 10f);
            Tag(sign, Kind.Taxi, zone, 0, "자율주행 택시");
        }

        // ---------- 리액터 시각 표현 ----------

        readonly Dictionary<int, GameObject> reactorBodies = new Dictionary<int, GameObject>();
        readonly Dictionary<int, Transform> reactorFill = new Dictionary<int, Transform>();
        readonly Dictionary<int, Renderer> reactorShell = new Dictionary<int, Renderer>();
        Material shellGrowing, shellReady, shellBad, shellEmpty, fillMat;

        public void SyncReactors(LabMeat.Sim.Game g)
        {
            if (shellGrowing == null)
            {
                shellGrowing = Shade(0.85f); shellReady = Shade(1f); shellBad = Shade(0.25f); shellEmpty = Shade(0.65f); fillMat = Shade(0.45f);
            }
            var alive = new HashSet<int>();
            foreach (var r in g.reactors)
            {
                alive.Add(r.id);
                GameObject body;
                if (!reactorBodies.TryGetValue(r.id, out body))
                {
                    var slot = reactorSlots[r.location][r.slot];
                    float scale = r.def == "pilot" ? 1.6f : 1f;
                    body = Prim(PrimitiveType.Cylinder, "리액터 본체 #" + r.id, slot.position + new Vector3(0, 0.9f * scale, 0), new Vector3(0.8f * scale, 0.85f * scale, 0.8f * scale), shellEmpty);
                    Object.Destroy(body.GetComponent<Collider>());
                    var fill = Prim(PrimitiveType.Cylinder, "배양물", slot.position, new Vector3(0.6f * scale, 0.01f, 0.6f * scale), fillMat);
                    Object.Destroy(fill.GetComponent<Collider>());
                    fill.transform.SetParent(body.transform, true);
                    var cap = Prim(PrimitiveType.Cylinder, "뚜껑", slot.position + new Vector3(0, 1.8f * scale, 0), new Vector3(0.85f * scale, 0.05f, 0.85f * scale), matBlack);
                    Object.Destroy(cap.GetComponent<Collider>());
                    cap.transform.SetParent(body.transform, true);
                    reactorBodies[r.id] = body;
                    reactorFill[r.id] = fill.transform;
                    reactorShell[r.id] = body.GetComponent<Renderer>();
                }
                float grow = r.state == LabMeat.Sim.ReactorState.Empty ? 0 : Mathf.Clamp01(r.growth);
                var s = reactorFill[r.id].localScale;
                reactorFill[r.id].localScale = new Vector3(s.x, Mathf.Max(0.01f, grow * 0.95f), s.z);
                var basePos = reactorSlots[r.location][r.slot].position;
                float sc = r.def == "pilot" ? 1.6f : 1f;
                reactorFill[r.id].position = basePos + new Vector3(0, 0.1f + grow * 0.85f * sc, 0);
                reactorShell[r.id].sharedMaterial =
                    r.state == LabMeat.Sim.ReactorState.Contaminated ? shellBad :
                    r.state == LabMeat.Sim.ReactorState.Ready ? shellReady :
                    r.state == LabMeat.Sim.ReactorState.Growing ? shellGrowing : shellEmpty;
            }
            foreach (var id in new List<int>(reactorBodies.Keys))
            {
                if (alive.Contains(id)) continue;
                Object.Destroy(reactorBodies[id]);
                reactorBodies.Remove(id); reactorFill.Remove(id); reactorShell.Remove(id);
            }
        }

        public void SyncSun(float hourOfDay)
        {
            if (sun == null) return;
            float h = hourOfDay % 24f;
            float a = (h - 6f) / 12f * 180f;              // 06시 일출, 18시 일몰
            sun.transform.rotation = Quaternion.Euler(Mathf.Clamp(a, -10f, 190f), -30f, 0);
            bool day = h >= 6f && h <= 19f;
            sun.intensity = day ? 1.0f : 0.05f;
            RenderSettings.ambientLight = day ? new Color(0.55f, 0.55f, 0.55f) : new Color(0.18f, 0.18f, 0.2f);
        }
    }
}
