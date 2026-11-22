// 판매: 지인 연락처·주문·배달·입소문, 레스토랑 주간 계약(Phase 2), 도시국가 수출(승인 후).
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabMeat.Sim
{
    public class Illness { public long at; public int contact; public string who; public int packs; public float refund; }

    public partial class Game
    {
        public List<ContactState> contacts = new List<ContactState>();
        public List<Order> orders = new List<Order>();
        public Dictionary<string, float> prices = new Dictionary<string, float>();
        public Contract contract;
        public List<Illness> illnesses = new List<Illness>();
        public float referenceMult = 1f;
        public int exportedToday;

        void InitSales()
        {
            for (int k = 0; k < B.contacts.Count; k++)
            {
                var d = B.contacts[k];
                contacts.Add(new ContactState { def = k, unlocked = k < B.start.startContacts, relationship = d.startRelationship, nextOrderAllowed = T + rng.Int(30, 240) });
            }
            foreach (var p in B.products) prices[p.id] = (float)Math.Round(p.referencePrice * 1.2f);
        }

        public ContactDef CDef(int k) { return B.contacts[k]; }

        public float UnitPrice(string product, string species) { return (float)Math.Round(prices[product] * SDef(species).priceMult, 1); }

        public float Tolerance(int contact, string product, string species)
        {
            return CDef(contact).priceTolerance * PDef(product).referencePrice * SDef(species).priceMult * referenceMult;
        }

        public float PriceFactor(float price, float tolerance)
        {
            float ratio = price / Math.Max(0.01f, tolerance);
            if (ratio > B.sales.maxPriceRatio) return 0;
            return Math.Min(1.5f, (float)Math.Pow(1f / ratio, B.sales.elasticity));
        }

        public int RegularCount { get { return contacts.Count(c => c.regular); } }

        void StepSales()
        {
            if (T % 60 == 0)
            {
                int h = Hour;
                if (h >= B.time.orderStartHour && h < B.time.orderEndHour) GenerateOrders();
            }
            foreach (var o in orders)
            {
                if (o.status == OrderStatus.Open && T > o.deadline)
                {
                    o.status = OrderStatus.Expired;
                    var c = contacts[o.contact];
                    c.relationship = Math.Max(0, c.relationship - 15);
                    c.failures++;
                    opinion = Math.Max(0, opinion - 0.5f);
                    Notify(CDef(o.contact).name, "주문한 고기가 결국 안 왔네. 실망이야.");
                }
            }
            foreach (var ill in illnesses.ToList())
            {
                if (T >= ill.at) { illnesses.Remove(ill); RaiseIllness(ill); }
            }
        }

        void GenerateOrders()
        {
            var prods = B.products.Where(p => ProductUnlocked(p.id)).ToList();
            var specs = B.species.Where(s => SpeciesUnlocked(s.id)).ToList();
            if (prods.Count == 0 || specs.Count == 0) return;
            int hours = Math.Max(1, B.time.orderEndHour - B.time.orderStartHour);
            for (int k = 0; k < contacts.Count; k++)
            {
                var c = contacts[k];
                if (!c.unlocked || T < c.nextOrderAllowed) continue;
                if (orders.Any(o => o.contact == k && o.status == OrderStatus.Open)) continue;
                var d = CDef(k);
                var prod = prods.Any(p => p.id == d.likes) && rng.Chance(0.7) ? PDef(d.likes) : rng.Pick(prods);
                var sp = rng.Pick(specs);
                float price = UnitPrice(prod.id, sp.id);
                float p = d.ordersPerDay / hours * (c.relationship / 60f) * PriceFactor(price, Tolerance(k, prod.id, sp.id)) * (0.5f + opinion / 100f);
                if (!rng.Chance(p)) continue;
                var o = new Order
                {
                    id = NewId(), contact = k, product = prod.id, species = sp.id, qty = rng.Int(1, 4), unitPrice = price,
                    created = T, deadline = T + B.sales.deadlineHours * 60, status = OrderStatus.Open,
                };
                orders.Add(o);
                c.nextOrderAllowed = T + 6 * 60;
                Notify(d.name, $"{sp.label} {prod.label} {o.qty}팩 부탁해요. 내일 {Fmt(o.deadline).Split(' ')[1]}까지 집으로. (팩당 ${price:0})");
            }
        }

        public List<Order> OpenOrders { get { return orders.Where(o => o.status == OrderStatus.Open).ToList(); } }

        public Order OpenOrderFor(int contact) { return orders.FirstOrDefault(o => o.contact == contact && o.status == OrderStatus.Open); }

        public string DeclineOrder(int orderId)
        {
            var o = orders.FirstOrDefault(x => x.id == orderId && x.status == OrderStatus.Open);
            if (o == null) return "없는 주문";
            o.status = OrderStatus.Declined;
            contacts[o.contact].relationship = Math.Max(0, contacts[o.contact].relationship - 3);
            return null;
        }

        // 지인 집 앞에서 배달
        public string Deliver(int contact, out string reply)
        {
            reply = null;
            var o = OpenOrderFor(contact);
            if (o == null) return "주문이 없다";
            var match = carried.Where(p => p.product == o.product && p.species == o.species).Take(o.qty).ToList();
            if (match.Count < o.qty) return $"{SDef(o.species).label} {PDef(o.product).label} {o.qty}팩이 필요하다 (들고 있는 것 {match.Count}팩)";
            foreach (var p in match) carried.Remove(p);
            float pay = o.unitPrice * o.qty;
            Earn(pay, "지인 판매");
            o.status = OrderStatus.Delivered;
            records.packsSold += o.qty;
            var c = contacts[contact];
            var pd = PDef(o.product);
            float q = match.Average(p => p.q.Score(pd.textureWeight));
            float sat = q - 50 - Math.Max(0, (o.unitPrice / Tolerance(contact, o.product, o.species) - 1f) * 30f);
            c.relationship = Math.Max(0, Math.Min(100, c.relationship + sat / 4f + 3f));
            c.deliveries++;
            int bad = match.Count(p => p.contaminatedHidden);
            if (bad > 0 && rng.Chance(B.sales.contamIllnessChance))
                illnesses.Add(new Illness { at = T + rng.Int(12 * 60, 60 * 60), contact = contact, who = CDef(contact).name + "님", packs = bad, refund = pay });
            if (!c.regular && c.deliveries >= B.sales.regularDeliveries && c.relationship >= B.sales.regularRelationship)
            {
                c.regular = true;
                Notify("AI", $"{CDef(contact).name}님이 단골이 되었습니다. 단골 {RegularCount}명");
            }
            reply = sat > 10 ? "와, 지난번보다 더 맛있어졌네!" : sat > -10 ? "고마워, 잘 먹을게." : "음… 솔직히 좀 아쉬워.";
            if (sat > 15) opinion = Math.Min(100, opinion + 0.3f);
            if (c.deliveries >= B.sales.referralMinDeliveries && c.relationship >= B.sales.referralMinRelationship && rng.Chance(B.sales.referralChance))
            {
                var next = contacts.FirstOrDefault(x => !x.unlocked);
                if (next != null)
                {
                    next.unlocked = true;
                    next.nextOrderAllowed = T + 120;
                    Notify(CDef(contact).name, $"{CDef(next.def).name}({CDef(next.def).relation})한테 네 고기 얘기했어. 연락 갈 거야.");
                }
            }
            return null;
        }

        void RaiseIllness(Illness ill)
        {
            Raise(new GameEvent
            {
                kind = "illness", title = "배치 오염 사고",
                text = $"{ill.who}이(가) 고기를 먹고 배탈이 났다고 연락했다. 오염된 배치가 판매된 것으로 보인다.",
                options = new[] { "즉시 공개하고 리콜한다 (여론 소폭 하락, 대금 환불)", "조용히 넘어간다 (발각 시 큰 하락)" },
                data = ill.contact, amount = ill.refund,
            });
        }

        void DailySales()
        {
            exportedToday = 0;
            if (contract != null && contract.active && day - contract.weekStartDay + 1 >= 7)
            {
                var o = contract.offer;
                if (contract.deliveredKg + 1e-3 >= o.kgPerWeek)
                {
                    contract.weeksFulfilled++;
                    ledger.notes.Add($"{o.name} 주간 납품 달성 ({contract.deliveredKg:0.0}/{o.kgPerWeek}kg)");
                }
                else
                {
                    Spend("위약금", o.penalty);
                    opinion = Math.Max(0, opinion - 2);
                    ledger.notes.Add($"{o.name} 주간 납품 미달 ({contract.deliveredKg:0.0}/{o.kgPerWeek}kg), 위약금 ${o.penalty:0}");
                }
                contract.weeksDone++;
                contract.deliveredKg = 0;
                contract.weekStartDay = day + 1;
                if (contract.weeksDone >= o.weeks)
                {
                    contract.active = false;
                    Notify(o.name, $"계약 기간이 끝났습니다. {contract.weeksFulfilled}/{o.weeks}주 달성.");
                }
            }
        }

        // ---------- 레스토랑 ----------

        public List<RestaurantOffer> RestaurantOffers { get { return phase >= 2 ? B.restaurant.offers : new List<RestaurantOffer>(); } }

        public string AcceptContract(int index)
        {
            if (phase < 2) return "Phase 2부터";
            if (contract != null && contract.active) return "진행 중인 계약이 있다";
            var o = B.restaurant.offers[index];
            int prevFulfilled = contract != null ? contract.weeksFulfilled : 0;
            contract = new Contract { offer = o, weekStartDay = day, active = true, weeksFulfilled = prevFulfilled };
            Notify(o.name, $"계약 체결. 매주 {o.kgPerWeek}kg, 품질 {o.minQuality} 이상, kg당 ${o.pricePerKg}.");
            return null;
        }

        public int TotalRestaurantWeeks { get { return contract != null ? contract.weeksFulfilled : 0; } }

        public string DeliverRestaurant(out float kg)
        {
            kg = 0;
            if (contract == null || !contract.active) return "진행 중인 계약이 없다";
            var o = contract.offer;
            var ok = carried.Where(p => p.q.Score(PDef(p.product).textureWeight) >= o.minQuality).ToList();
            if (ok.Count == 0) return $"품질 {o.minQuality} 이상인 팩이 없다";
            foreach (var p in ok)
            {
                carried.Remove(p);
                kg += PDef(p.product).kgPerPack;
                if (p.contaminatedHidden && rng.Chance(B.sales.contamIllnessChance))
                    illnesses.Add(new Illness { at = T + rng.Int(12 * 60, 48 * 60), contact = -1, who = o.name + " 손님", packs = 1, refund = PDef(p.product).kgPerPack * o.pricePerKg });
            }
            contract.deliveredKg += kg;
            records.packsSold += ok.Count;
            Earn(kg * o.pricePerKg, "레스토랑 납품");
            return null;
        }

        // ---------- 수출 (승인 후) ----------

        public string Export(int packs)
        {
            if (approval != ApprovalState.Approved) return "국가 승인 필요";
            int n = Math.Min(packs, Math.Min(fridge.Count, B.sales.exportDailyCap - exportedToday));
            if (n <= 0) return "오늘 수출 한도를 채웠거나 재고가 없다";
            var sold = fridge.OrderByDescending(p => p.q.safety).Take(n).ToList();
            foreach (var p in sold) fridge.Remove(p);
            exportedToday += n;
            records.packsSold += n;
            Earn(n * B.sales.exportPricePerPack, "수출");
            return null;
        }
    }
}
