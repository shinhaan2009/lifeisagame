// 채용: 숨은 능력치, 면접 질문으로 추정 범위 좁히기, 검증(실기·평판), 연봉 협상, 근무 중 정보 공개.
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabMeat.Sim
{
    public partial class Game
    {
        public static readonly string[] Stats = { "skill", "speed", "care", "research", "reliability" };
        public static readonly Dictionary<string, string> StatLabel = new Dictionary<string, string>
        {
            { "skill", "배양 숙련도" }, { "speed", "작업 속도" }, { "care", "꼼꼼함" }, { "research", "연구 적성" }, { "reliability", "신뢰도" },
        };
        public static readonly Dictionary<string, string> RoleLabel = new Dictionary<string, string>
        {
            { "culture", "배양" }, { "processing", "가공" }, { "research", "연구" },
        };

        public List<Candidate> candidates = new List<Candidate>();
        public List<Employee> employees = new List<Employee>();
        int nextCandidateDay = 1;

        void InitHiring() { }

        public TraitDef TDef(string id) { return B.hiring.traits.First(t => t.id == id); }
        public QuestionDef QDef(string id) { return B.hiring.questions.First(q => q.id == id); }

        static int ClampStat(float v) { return Math.Max(1, Math.Min(10, (int)Math.Round(v))); }

        void SpawnCandidates()
        {
            var H = B.hiring;
            for (int k = 0; k < H.candidatesPerBatch; k++)
            {
                var c = new Candidate { id = NewId(), name = rng.Pick(H.names) };
                foreach (var s in Stats) c.stats[s] = ClampStat(rng.Normal(5.5f, 2f));
                foreach (var t in H.traits)
                {
                    if (c.traits.Count >= 2) break;
                    if (t.id == "insider" && phase < 3) continue;
                    if (rng.Chance(t.chance)) c.traits.Add(t.id);
                }
                if (c.traits.Contains("perfectionist")) { c.stats["care"] = ClampStat(c.stats["care"] + 2); c.stats["speed"] = ClampStat(c.stats["speed"] - 2); }
                foreach (var s in Stats)
                {
                    int inflate = rng.Chance(0.4) ? rng.Int(1, 4) : rng.Int(0, 2);
                    c.claims[s] = ClampStat(c.stats[s] + inflate);
                    c.est[s] = new[] { Math.Max(1f, c.claims[s] - 4f), Math.Min(10f, c.claims[s] + 1f) };
                }
                float r = (float)rng.Next();
                c.presentation = r < 0.3f ? -1.5f : r < 0.7f ? 0f : 1.5f;
                float sum = c.stats.Values.Sum();
                c.asking = (float)Math.Round((H.baseSalary + H.salaryPerStat * sum) * rng.Range(0.9f, 1.1f) * (c.traits.Contains("ambitious") ? 1.15f : 1f));
                c.offerDeadlineDay = day + rng.Int(3, 7);
                candidates.Add(c);
            }
            Notify("AI", $"새 지원자 {H.candidatesPerBatch}명의 이력서가 도착했습니다.");
        }

        void DailyHiring()
        {
            if (phase < B.hiring.phase) return;
            foreach (var c in candidates)
                if (!c.gone && day + 1 > c.offerDeadlineDay) { c.gone = true; Notify("채용", $"{c.name}은(는) 다른 회사 제안을 받아들였다."); }
            candidates.RemoveAll(c => c.gone);
            if (day + 1 >= nextCandidateDay) { SpawnCandidates(); nextCandidateDay = day + 1 + B.hiring.refreshDays; }
            foreach (var c in candidates)
            {
                if (c.referencePending && day + 1 >= c.referenceReadyDay)
                {
                    c.referencePending = false; c.referenceDone = true;
                    float v = c.stats["reliability"]; c.est["reliability"] = new[] { v, v };
                    c.transcript.Add($"평판 조회 결과: 신뢰도 {v:0}");
                }
            }
        }

        // ---------- 면접 ----------

        public string ScheduleInterview(int candId, long minute)
        {
            var c = candidates.FirstOrDefault(x => x.id == candId);
            if (c == null) return "없는 지원자";
            if (phase < B.hiring.phase) return $"Phase {B.hiring.phase}부터";
            if (minute <= T) return "이미 지난 시간";
            c.interviewMinute = minute;
            c.interviewLocation = "lab";
            Notify("채용", $"{c.name} 면접: {Fmt(minute)}, 공유 실험실");
            return null;
        }

        public bool IsWaiting(Candidate c) { return !c.gone && !c.interviewed && c.interviewMinute >= 0 && T >= c.interviewMinute && T < c.interviewMinute + 120; }

        public List<Candidate> WaitingCandidates(string location)
        {
            return candidates.Where(c => (IsWaiting(c) || c.inInterview) && c.interviewLocation == location).ToList();
        }

        public string StartInterview(int candId)
        {
            var c = candidates.FirstOrDefault(x => x.id == candId);
            if (c == null || !IsWaiting(c)) return "면접 대기 중이 아니다";
            c.inInterview = true; c.minutesUsed = 0;
            return null;
        }

        public int MinutesLeft(Candidate c) { return B.hiring.interviewMinutes - c.minutesUsed; }

        public string Ask(int candId, string questionId, out string answer)
        {
            answer = null;
            var c = candidates.FirstOrDefault(x => x.id == candId);
            if (c == null || !c.inInterview) return "면접 중이 아니다";
            var q = QDef(questionId);
            if (c.asked.Contains(questionId)) return "이미 한 질문";
            if (q.minutes > MinutesLeft(c)) return "면접 시간이 부족하다";
            c.asked.Add(questionId);
            c.minutesUsed += q.minutes;
            Advance(q.minutes);
            var H = B.hiring;
            if (q.kind == "career")
            {
                int maxInflate = Stats.Max(s => c.claims[s] - c.stats[s]);
                c.careerChecked = true;
                if (maxInflate >= 2)
                {
                    answer = "경력 질문에 답이 자꾸 엇갈린다. 이력서 일부가 과장된 것 같다.";
                    foreach (var s in Stats) c.est[s][1] = Math.Max(c.est[s][0], c.est[s][1] - 1.5f);
                }
                else answer = "이력서 내용과 답이 일치한다. 과장은 없어 보인다.";
            }
            else
            {
                float total = 0;
                foreach (var s in q.targets)
                {
                    float obs = c.stats[s] + c.presentation + rng.Normal(0, H.answerNoise);
                    total += obs;
                    var e = c.est[s];
                    float lo = Math.Max(e[0], obs - H.narrowHalfWidth), hi = Math.Min(e[1], obs + H.narrowHalfWidth);
                    if (lo > hi) { lo = obs - 0.5f; hi = obs + 0.5f; }
                    c.est[s] = new[] { Math.Max(1f, Math.Min(10f, lo)), Math.Max(1f, Math.Min(10f, hi)) };
                }
                float avg = total / q.targets.Length;
                answer = avg < 4 ? q.low : avg < 7 ? q.mid : q.high;
            }
            c.transcript.Add($"Q. {q.text}\nA. {answer}");
            return null;
        }

        public void EndInterview(int candId)
        {
            var c = candidates.FirstOrDefault(x => x.id == candId);
            if (c == null) return;
            c.inInterview = false; c.interviewed = true;
            float acc = B.compute[computeTier].aiAccuracy;
            var parts = new List<string>();
            foreach (var s in Stats)
            {
                float mid = (c.est[s][0] + c.est[s][1]) / 2f;
                if (rng.Chance(acc)) mid -= c.presentation * 0.7f;
                mid += rng.Normal(0, (1 - acc) * 2f);
                parts.Add($"{StatLabel[s]} ≈ {Math.Max(1, Math.Min(10, mid)):0}");
            }
            string tone = c.presentation > 0 && rng.Chance(acc) ? " 말솜씨가 좋아 실제보다 높게 보였을 수 있습니다."
                        : c.presentation < 0 && rng.Chance(acc) ? " 긴장해서 실제보다 낮게 보였을 수 있습니다." : "";
            c.aiNote = "AI 분석: " + string.Join(", ", parts) + "." + tone;
        }

        public string PracticalTest(int candId)
        {
            var c = candidates.FirstOrDefault(x => x.id == candId);
            if (c == null || (!c.inInterview && !c.interviewed)) return "면접 후에 할 수 있다";
            if (c.practicalDone) return "이미 했다";
            if (!CanAfford(B.hiring.practicalCost)) return "현금 부족";
            Spend("채용", B.hiring.practicalCost);
            Advance(B.hiring.practicalMinutes);
            c.practicalDone = true;
            foreach (var s in new[] { "skill", "care" }) { float v = c.stats[s]; c.est[s] = new[] { v, v }; }
            c.transcript.Add($"실기 테스트: 배양 숙련도 {c.stats["skill"]}, 꼼꼼함 {c.stats["care"]}");
            return null;
        }

        public string ReferenceCheck(int candId)
        {
            var c = candidates.FirstOrDefault(x => x.id == candId);
            if (c == null) return "없는 지원자";
            if (c.referenceDone || c.referencePending) return "이미 요청했다";
            if (!CanAfford(B.hiring.referenceCost)) return "현금 부족";
            Spend("채용", B.hiring.referenceCost);
            c.referencePending = true; c.referenceReadyDay = day + 1;
            return null;
        }

        public string Offer(int candId, float salary, out bool accepted)
        {
            accepted = false;
            var c = candidates.FirstOrDefault(x => x.id == candId);
            if (c == null) return "없는 지원자";
            if (!c.interviewed) return "면접을 먼저 봐야 한다";
            double p = salary >= c.asking ? 1 : salary >= c.asking * 0.9f ? 0.5 : salary >= c.asking * 0.8f ? 0.15 : 0;
            if (!rng.Chance(p))
            {
                c.transcript.Add($"${salary:0}/일 제안 거절 (요구 ${c.asking:0} 근처)");
                if (rng.Chance(0.3)) { c.gone = true; candidates.Remove(c); Notify("채용", $"{c.name}이(가) 협상을 끝냈다."); }
                return null;
            }
            accepted = true;
            var e = new Employee { id = NewId(), name = c.name, salary = salary, traits = c.traits, location = "lab" };
            foreach (var s in Stats) { e.stats[s] = c.stats[s]; e.est[s] = (float[])c.est[s].Clone(); }
            foreach (var t in c.traits) e.revealDay[t] = day + rng.Int(5, 15);
            employees.Add(e);
            candidates.Remove(c);
            Notify("채용", $"{e.name} 입사. 일급 ${salary:0}");
            return null;
        }

        public void SetRole(int empId, string role, string axis = null)
        {
            var e = employees.FirstOrDefault(x => x.id == empId);
            if (e == null) return;
            e.role = role;
            if (axis != null) e.axis = axis;
        }

        public void SetEmployeeLocation(int empId, string loc)
        {
            var e = employees.FirstOrDefault(x => x.id == empId);
            if (e != null) e.location = loc;
        }

        public void Fire(int empId) { employees.RemoveAll(e => e.id == empId); }

        // ---------- 근무 ----------

        void StepHiring()
        {
            foreach (var c in candidates)
            {
                if (c.interviewMinute == T) Notify("채용", $"{c.name}이(가) 공유 실험실에 도착했다. 2시간 기다린다.");
                if (!c.gone && !c.interviewed && !c.inInterview && c.interviewMinute >= 0 && T == c.interviewMinute + 120)
                {
                    c.gone = true;
                    Notify("채용", $"{c.name}이(가) 기다리다 돌아갔다.");
                }
            }
        }

        void PaySalaries()
        {
            float s = employees.Sum(e => e.salary);
            Spend("인건비", s);
        }

        void MorningEmployees()
        {
            foreach (var e in employees.ToList())
            {
                e.absentToday = rng.Chance((10 - e.stats["reliability"]) * 0.015);
                if (e.absentToday) Notify(e.name, "오늘 몸이 안 좋아서 못 나갈 것 같습니다.");
                else e.daysWorked++;
                foreach (var s in Stats)
                {
                    float v = e.stats[s];
                    e.est[s][0] += (v - e.est[s][0]) * 0.2f;
                    e.est[s][1] -= (e.est[s][1] - v) * 0.2f;
                }
                foreach (var t in e.traits)
                {
                    if (!e.knownTraits.Contains(t) && day >= e.revealDay[t])
                    {
                        e.knownTraits.Add(t);
                        Notify("AI", $"{e.name}의 특성이 드러났습니다: {TDef(t).label}. {TDef(t).desc}");
                    }
                }
                if (e.traits.Contains("ambitious") && !e.raiseAsked && e.daysWorked >= B.events.raiseRequestDay)
                {
                    e.raiseAsked = true;
                    Raise(new GameEvent
                    {
                        kind = "raise", title = "연봉 인상 요구", data = e.id,
                        text = $"{e.name}이(가) 일급을 ${e.salary * 1.2f:0}로 올려 달라고 한다. 거절하면 떠날 수 있다.",
                        options = new[] { "올려 준다", "거절한다" },
                    });
                }
            }
        }

        void ResolveRaise(int empId, bool accept)
        {
            var e = employees.FirstOrDefault(x => x.id == empId);
            if (e == null) return;
            if (accept) e.salary *= 1.2f;
            else if (rng.Chance(0.6)) { employees.Remove(e); Notify("채용", $"{e.name}이(가) 퇴사했다."); }
        }

        IEnumerable<Employee> Working(string role, string location = null)
        {
            int h = Hour;
            return employees.Where(e => !e.absentToday && e.role == role && (location == null || e.location == location) && h >= 9 && h < 18);
        }

        void HourlyEmployees()
        {
            foreach (var e in Working("culture").ToList())
            {
                int tasks = 1 + e.stats["speed"] / 4;
                foreach (var r in reactors.Where(r => r.location == e.location).ToList())
                {
                    if (tasks <= 0) break;
                    if (r.state == ReactorState.Contaminated) { Discard(r); tasks--; continue; }
                    if (r.contaminated && rng.Chance(e.stats["care"] / 14f))
                    {
                        r.state = ReactorState.Contaminated;
                        Notify(e.name, $"{LDef(r.location).label} {r.slot + 1}번 리액터가 오염된 것 같습니다. 폐기하겠습니다.");
                        tasks--; continue;
                    }
                    if (r.state == ReactorState.Ready) { string err; Harvest(r, out err); tasks--; continue; }
                    if ((r.state == ReactorState.Growing) && r.freshness < 45 && ChangeMedium(r) == null) { tasks--; continue; }
                    if (r.state == ReactorState.Empty && Inoculate(r, BestSpecies()) == null) { tasks--; continue; }
                }
            }
            foreach (var e in Working("processing").ToList())
            {
                if (BenchAt(e.location) != null) continue;
                var lot = lots.FirstOrDefault();
                if (lot == null) continue;
                StartBench(e.location, lot.id, BestProduct(), e.stats["speed"] / 5f);
            }
        }

        string BestSpecies()
        {
            return B.species.Where(s => SpeciesUnlocked(s.id)).OrderByDescending(s => s.priceMult).First().id;
        }

        string BestProduct()
        {
            return B.products.Where(p => ProductUnlocked(p.id)).OrderByDescending(p => p.referencePrice / p.kgPerPack).First().id;
        }

        public float StaffResearchPoints(string axis)
        {
            float p = 0;
            foreach (var e in employees)
            {
                if (e.absentToday || e.role != "research" || e.axis != axis) continue;
                float m = e.traits.Contains("aiDistrust") ? 0.7f : 1f;
                p += e.stats["research"] * B.researchModel.staffPointsPerAptitude * m;
            }
            return p;
        }

        public float StaffYieldMult(string location)
        {
            var w = employees.Where(e => !e.absentToday && e.role == "culture" && e.location == location).ToList();
            if (w.Count == 0) return 1f;
            return 1f + 0.03f * (float)(w.Average(e => e.stats["skill"]) - 5);
        }

        public float StaffContamMult(string location)
        {
            var w = employees.Where(e => !e.absentToday && e.role == "culture" && e.location == location).ToList();
            if (w.Count == 0) return 1f;
            return Math.Max(0.5f, Math.Min(1.3f, 1f - 0.06f * (w.Max(e => e.stats["care"]) - 5)));
        }
    }
}
