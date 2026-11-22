// 생산: 리액터 배치, 작업대 가공, 냉장고, 재료 주문과 택배.
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabMeat.Sim
{
    public partial class Game
    {
        public const float FreshDecayPerMinute = 100f / (24f * 60f);

        public Reactor AddReactor(string defId, string location)
        {
            int slots = LDef(location).reactorSlots;
            int slot = Enumerable.Range(0, slots).FirstOrDefault(i => reactors.All(r => !(r.location == location && r.slot == i)));
            if (reactors.Any(r => r.location == location && r.slot == slot)) return null;
            var r2 = new Reactor { id = NewId(), def = defId, location = location, slot = slot, state = ReactorState.Empty, boughtDay = day };
            reactors.Add(r2);
            return r2;
        }

        public Reactor ReactorAt(string location, int slot)
        {
            return reactors.FirstOrDefault(r => r.location == location && r.slot == slot);
        }

        public float MediumUnitPrice
        {
            get { return Mat("medium").price * (float)Math.Pow(B.researchModel.mediumCostPerSerumFree, Level("serumFree")); }
        }

        public float MaterialPrice(string id) { return id == "medium" ? MediumUnitPrice : Mat(id).price; }

        public bool SpeciesUnlocked(string id) { return Level("cellLine") >= SDef(id).cellLineRequired; }

        public bool ProductUnlocked(string id)
        {
            var p = PDef(id);
            if (p.locked == 1) return false;
            return string.IsNullOrEmpty(p.unlockAxis) || Level(p.unlockAxis) >= p.unlockLevel;
        }

        // ---------- 리액터 조작 ----------

        public string Inoculate(Reactor r, string species)
        {
            var d = RDef(r.def);
            if (r.state != ReactorState.Empty) return "이미 배양 중이다";
            if (!SpeciesUnlocked(species)) return "아직 다룰 수 없는 세포주다";
            if (stock["vial"] < 1) return "세포 바이알이 없다";
            if (stock["medium"] < d.capacityL) return $"배지 {d.capacityL}L 필요";
            stock["vial"]--;
            stock["medium"] -= d.capacityL;
            r.state = ReactorState.Growing;
            r.species = species;
            r.startMinute = T;
            r.growth = 0; r.freshness = 100; r.freshSum = 0; r.freshSamples = 0;
            r.contaminated = false; r.overripeHours = 0; r.lastCheck = "";
            r.batchCost = Mat("vial").price + MediumUnitPrice * d.capacityL;
            return null;
        }

        public string ChangeMedium(Reactor r)
        {
            var d = RDef(r.def);
            if (r.state != ReactorState.Growing && r.state != ReactorState.Ready) return "배양 중이 아니다";
            if (stock["medium"] < d.capacityL) return $"배지 {d.capacityL}L 필요";
            stock["medium"] -= d.capacityL;
            r.freshness = 100;
            r.batchCost += MediumUnitPrice * d.capacityL;
            return null;
        }

        // 온도·pH 점검. 오염은 시간이 지날수록 수치에 드러난다.
        public string Check(Reactor r)
        {
            if (r.state == ReactorState.Empty) return r.lastCheck = "비어 있음";
            float temp = 37.0f + rng.Range(-0.1f, 0.1f);
            float ph = 7.20f + rng.Range(-0.04f, 0.04f);
            string extra = "";
            if (r.contaminated)
            {
                float hours = (T - r.contamMinute) / 60f;
                float show = Math.Min(1f, 0.15f + hours / 12f);
                if (rng.Chance(show))
                {
                    ph -= 0.25f + hours * 0.02f;
                    temp += 0.4f;
                    extra = " · 배지가 탁하다";
                }
            }
            r.lastCheck = $"온도 {temp:0.0}°C · pH {ph:0.00} · 배지 신선도 {r.freshness:0}%{extra}";
            return r.lastCheck;
        }

        public string Discard(Reactor r)
        {
            if (r.state == ReactorState.Empty) return "비어 있다";
            records.batches.Add(false);
            ledger.notes.Add($"리액터 #{r.id} 배치 폐기");
            r.state = ReactorState.Empty;
            r.contaminated = false;
            r.growth = 0;
            r.lastCheck = "";
            return null;
        }

        public RawLot Harvest(Reactor r, out string error)
        {
            error = null;
            if (r.state != ReactorState.Ready) { error = "아직 수확할 수 없다"; return null; }
            var d = RDef(r.def);
            var sp = SDef(r.species);
            float avgFresh = r.freshSamples > 0 ? r.freshSum / r.freshSamples : 100f;
            float freshMult = 0.6f + 0.4f * (avgFresh / 100f);
            float overripe = Math.Max(0.4f, 1f - Math.Max(0, r.overripeHours - 24f) * 0.015f);
            float kg = d.baseYieldKg * sp.yieldMult * (1f + B.researchModel.yieldPerCellLine * Level("cellLine")) * freshMult * overripe * StaffYieldMult(r.location);
            if (r.contaminated && rng.Chance(0.5))
            {
                error = "수확 중 이상한 냄새. 오염된 배치라 폐기했다";
                ledger.notes.Add($"리액터 #{r.id} 수확 중 오염 발견, 폐기");
                r.state = ReactorState.Empty; r.contaminated = false; r.growth = 0;
                records.batches.Add(false);
                return null;
            }
            var M = B.researchModel;
            var q = new Quality
            {
                taste = Clamp(40 + sp.tasteBonus + M.tasteCellLine * Level("cellLine") + (avgFresh - 70) * 0.3f + rng.Range(-4, 4)),
                texture = Clamp(30 + M.textureScaffold * Level("scaffold") + M.textureVascular * Level("vascular") + rng.Range(-4, 4)),
                safety = r.contaminated ? rng.Range(5, 20) : Clamp(80 + M.safetySterility * Level("sterility") + rng.Range(-3, 3)),
            };
            var lot = new RawLot
            {
                id = NewId(), species = r.species, kg = (float)Math.Round(kg, 2), q = q, contaminatedHidden = r.contaminated,
                cost = r.batchCost + (float)RDef(r.def).price / Math.Max(1, d.lifeDays) * ((T - r.startMinute) / 1440f), day = day, location = r.location,
            };
            lots.Add(lot);
            records.batches.Add(!r.contaminated);
            if (!r.contaminated && lot.kg > 0) records.lowestCostPerKg = Math.Min(records.lowestCostPerKg, lot.cost / lot.kg);
            r.state = ReactorState.Empty; r.growth = 0; r.contaminated = false; r.lastCheck = "";
            return lot;
        }

        static float Clamp(float v) { return Math.Max(0, Math.Min(100, v)); }

        public float CostPerKg(RawLot lot) { return lot.kg > 0 ? lot.cost / lot.kg : 0; }

        // ---------- 작업대 ----------

        public BenchJob BenchAt(string location) { return jobs.FirstOrDefault(j => j.location == location); }

        public int PacksFrom(RawLot lot, string product) { return (int)Math.Floor(lot.kg / PDef(product).kgPerPack + 1e-4); }

        public string StartBench(string location, int lotId, string product, float speedMult = 1f)
        {
            if (BenchAt(location) != null) return "작업대가 사용 중이다";
            var lot = lots.FirstOrDefault(l => l.id == lotId);
            if (lot == null) return "수확물이 없다";
            if (!ProductUnlocked(product)) return "아직 만들 수 없는 제품이다";
            int packs = PacksFrom(lot, product);
            if (packs < 1) return "양이 너무 적다";
            if (stock["packaging"] < packs) return $"포장재 {packs}개 필요";
            stock["packaging"] -= packs;
            lots.Remove(lot);
            var pd = PDef(product);
            float minutes = packs * pd.minutesPerPack / Math.Max(0.2f, speedMult);
            var q = lot.q;
            if (product == "nugget") q.texture = Clamp(q.texture + 12);
            jobs.Add(new BenchJob
            {
                location = location, lotId = lotId, product = product, packs = packs, minutesLeft = minutes, totalMinutes = minutes,
                species = lot.species, q = q, contam = lot.contaminatedHidden,
                costPerPack = lot.cost / packs + Mat("packaging").price,
            });
            return null;
        }

        void FinishJob(BenchJob j)
        {
            for (int k = 0; k < j.packs; k++)
            {
                fridge.Add(new Pack { id = NewId(), product = j.product, species = j.species, q = j.q, contaminatedHidden = j.contam, cost = j.costPerPack, madeMinute = T });
            }
            jobs.Remove(j);
            Notify("작업대", $"{SDef(j.species).label} {PDef(j.product).label} {j.packs}팩 포장 완료, 냉장고에 보관");
        }

        // ---------- 냉장고 / 휴대 ----------

        public string TakeFromFridge(int packId)
        {
            if (carried.Count >= carryLimit) return $"더 들 수 없다 (최대 {carryLimit}팩)";
            var p = fridge.FirstOrDefault(x => x.id == packId);
            if (p == null) return "없다";
            fridge.Remove(p);
            p.takenMinute = T;
            carried.Add(p);
            return null;
        }

        public string PutInFridge(int packId)
        {
            var p = carried.FirstOrDefault(x => x.id == packId);
            if (p == null) return "없다";
            carried.Remove(p);
            fridge.Add(p);
            return null;
        }

        public float HoursLeft(Pack p)
        {
            float shelf = B.sales.fridgeShelfHours - (T - p.madeMinute) / 60f;
            if (p.takenMinute >= 0) shelf = Math.Min(shelf, B.sales.carryShelfHours - (T - p.takenMinute) / 60f);
            return shelf;
        }

        // ---------- 재료 / 장비 구매 ----------

        public float OrderCost(Dictionary<string, int> items)
        {
            float c = 0;
            foreach (var kv in items) c += MaterialPrice(kv.Key) * kv.Value;
            return c;
        }

        public string OrderMaterials(Dictionary<string, int> items)
        {
            float c = OrderCost(items);
            if (c <= 0) return "주문할 것이 없다";
            if (!CanAfford(c)) return "현금 부족";
            Spend("재료", c);
            var p = new Parcel { arrivalMinute = (long)day * 1440 + B.time.parcelArrivalHour * 60 };
            foreach (var kv in items) if (kv.Value > 0) p.items[kv.Key] = kv.Value;
            parcels.Add(p);
            return null;
        }

        public List<Parcel> ArrivedParcels { get { return parcels.Where(p => T >= p.arrivalMinute).ToList(); } }

        public string PickUpParcels()
        {
            var arrived = ArrivedParcels;
            if (arrived.Count == 0) return "도착한 택배가 없다";
            foreach (var p in arrived)
            {
                foreach (var kv in p.items) stock[kv.Key] += kv.Value;
                parcels.Remove(p);
            }
            return null;
        }

        public string BuyReactor(string defId, string location)
        {
            var d = RDef(defId);
            var loc = LDef(location);
            if (d.locked == 1) return "v0.2 이후 제공";
            if (phase < d.phase) return $"Phase {d.phase}부터 구매 가능";
            if (phase < loc.phase) return $"{loc.label}은 Phase {loc.phase}부터";
            if (reactors.Count(r => r.location == location) >= loc.reactorSlots) return $"{loc.label} 자리가 없다";
            if (!CanAfford(d.price)) return "현금 부족";
            Spend("설비", d.price);
            AddReactor(defId, location);
            return null;
        }

        // ---------- 분 단위 진행 ----------

        void StepProduction()
        {
            foreach (var r in reactors)
            {
                if (r.state == ReactorState.Growing || r.state == ReactorState.Ready)
                {
                    r.freshness = Math.Max(0, r.freshness - FreshDecayPerMinute);
                    r.freshSum += r.freshness; r.freshSamples++;
                }
                if (r.state == ReactorState.Growing)
                {
                    var d = RDef(r.def);
                    float rate = 1f / (d.cultureHours * 60f);
                    if (r.freshness < 30) rate *= 0.5f;
                    if (r.contaminated) rate *= 0.6f;
                    r.growth += rate;
                    if (r.growth >= 1f)
                    {
                        r.growth = 1f; r.state = ReactorState.Ready;
                        Notify("리액터", $"{LDef(r.location).label} 리액터 {r.slot + 1}번 수확 가능");
                    }
                }
                if (r.state == ReactorState.Ready) r.overripeHours += 1f / 60f;
                if (T % 60 == 0 && (r.state == ReactorState.Growing || r.state == ReactorState.Ready) && !r.contaminated)
                {
                    if (rng.Chance(ContamChancePerHour(r))) { r.contaminated = true; r.contamMinute = T; }
                }
            }
            for (int k = jobs.Count - 1; k >= 0; k--)
            {
                var j = jobs[k];
                j.minutesLeft -= 1;
                if (j.minutesLeft <= 0) FinishJob(j);
            }
            foreach (var p in parcels)
            {
                if (!p.notified && T >= p.arrivalMinute) { p.notified = true; Notify("택배", "주문한 재료가 집 앞 택배함에 도착했다"); }
            }
            if (T % 30 == 0) Spoilage();
        }

        public float ContamChancePerHour(Reactor r)
        {
            var d = RDef(r.def);
            float p = d.contamPerHour * Math.Max(0.15f, 1f - B.researchModel.contamPerSterility * Level("sterility"));
            if (r.freshness < 30) p *= 3f; else if (r.freshness < 60) p *= 1.5f;
            p *= StaffContamMult(r.location);
            return p;
        }

        void Spoilage()
        {
            int spoiled = 0;
            spoiled += fridge.RemoveAll(p => HoursLeft(p) <= 0);
            spoiled += carried.RemoveAll(p => HoursLeft(p) <= 0);
            if (spoiled > 0) Notify("냉장고", $"유통기한이 지난 {spoiled}팩을 버렸다");
        }
    }
}
