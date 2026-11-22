// 밸런스 설정 구조. 값은 Assets/Resources/balance.json 에 있다.
using System;
using System.Collections.Generic;

namespace LabMeat.Sim
{
    [Serializable] public class Balance
    {
        public TimeCfg time;
        public StartCfg start;
        public CostCfg costs;
        public List<MaterialDef> materials;
        public List<ReactorDef> reactors;
        public List<SpeciesDef> species;
        public List<ProductDef> products;
        public List<AxisDef> research;
        public ResearchCfg researchModel;
        public List<ComputeTier> compute;
        public SalesCfg sales;
        public List<ContactDef> contacts;
        public RestaurantCfg restaurant;
        public List<RoundDef> rounds;
        public ApprovalCfg approval;
        public HiringCfg hiring;
        public EventCfg events;
        public List<NewsTemplate> news;
        public List<LocationDef> locations;
    }

    [Serializable] public class TimeCfg
    {
        public int wakeHour = 7;            // 기상 시각
        public int earliestSleepHour = 18;  // 이보다 이르면 잠들 수 없음
        public int forcedSleepHour = 26;    // 26 = 다음 날 02:00, 이때 기절
        public int parcelArrivalHour = 9;   // 다음 날 택배 도착
        public int orderStartHour = 8;
        public int orderEndHour = 20;
        public float realSecondsPerGameMinute = 1f;
    }

    [Serializable] public class StartCfg
    {
        public float cash;
        public float opinion;
        public int startContacts;
        public Dictionary<string, int> materials;
        public string startReactor;
        public int carryLimit;
    }

    [Serializable] public class CostCfg
    {
        public float livingPerDay;
        public float energyPerReactorPerDay;
        public float marketingCost;
        public float marketingOpinion;
        public float taxiFare;
        public int taxiMinutes;
    }

    [Serializable] public class MaterialDef
    {
        public string id; public string label; public string unit; public float price; public int orderStep;
    }

    [Serializable] public class ReactorDef
    {
        public string id; public string label;
        public float price; public int phase; public int lifeDays;
        public int capacityL;          // 배지 1회 교체량
        public float cultureHours;
        public float baseYieldKg;
        public float contamPerHour;    // 기본 시간당 오염 확률
        public int locked;             // 1 = MVP 범위 밖
    }

    [Serializable] public class SpeciesDef
    {
        public string id; public string label;
        public int cellLineRequired;
        public float yieldMult; public float priceMult; public float tasteBonus;
    }

    [Serializable] public class ProductDef
    {
        public string id; public string label;
        public float kgPerPack; public int minutesPerPack;
        public string unlockAxis; public int unlockLevel; public string unlockLabel;
        public float referencePrice;   // 지인 기준 가격 (팩당)
        public float textureWeight;    // 품질에서 질감 비중
        public int locked;
    }

    [Serializable] public class AxisDef
    {
        public string id; public string label; public string problem; public string theory; public string effect;
        public float baseDifficulty; public float difficultyGrowth; public int maxLevel;
    }

    [Serializable] public class ResearchCfg
    {
        public float breakthroughShare;   // 성공 중 돌파 비율
        public float partialProgress;     // 부분 성공 진행
        public float failKeep;            // 실패 시 누적 포인트 유지 비율
        public float staffPointsPerAptitude;
        public float hiddenMultMin; public float hiddenMultMax;
        public float yieldPerCellLine;
        public float mediumCostPerSerumFree;   // 레벨당 배지 단가 배수
        public float contamPerSterility;
        public float textureScaffold; public float textureVascular; public float tasteCellLine; public float safetySterility;
        public float newsBuffMult; public int newsBuffDays;
    }

    [Serializable] public class ComputeTier
    {
        public string id; public string label; public float price; public float perDay; public float researchMult; public float aiAccuracy; public int phase;
    }

    [Serializable] public class SalesCfg
    {
        public float elasticity;
        public float maxPriceRatio;
        public int deadlineHours;
        public float contamIllnessChance;
        public float referralChance;
        public int referralMinDeliveries;
        public float referralMinRelationship;
        public int regularDeliveries; public float regularRelationship;
        public int fridgeShelfHours; public int carryShelfHours;
        public float exportPricePerPack; public int exportDailyCap;
    }

    [Serializable] public class ContactDef
    {
        public string name; public string relation; public int house;
        public float ordersPerDay; public float priceTolerance; public float startRelationship;
        public string likes;
    }

    [Serializable] public class RestaurantCfg
    {
        public List<RestaurantOffer> offers;
    }

    [Serializable] public class RestaurantOffer
    {
        public string name; public float kgPerWeek; public float minQuality; public float pricePerKg; public float penalty; public int weeks;
    }

    [Serializable] public class RoundDef
    {
        public string id; public string label; public float amount; public float equity; public int toPhase;
        public int deadlineDays; public string condition;
        // 조건 수치
        public int packsSold; public int regulars; public int restaurantWeeks; public float batchSuccess; public int approvals;
    }

    [Serializable] public class ApprovalCfg
    {
        public string country; public float fee; public float baseDays; public float contamDelayDays; public float lobbyDelayDays; public int phase;
    }

    [Serializable] public class HiringCfg
    {
        public int phase; public int candidatesPerBatch; public int refreshDays;
        public int interviewMinutes; public float answerNoise; public float narrowHalfWidth;
        public float practicalCost; public int practicalMinutes; public float referenceCost;
        public float baseSalary; public float salaryPerStat;
        public List<string> names;
        public List<TraitDef> traits;
        public List<QuestionDef> questions;
    }

    [Serializable] public class TraitDef
    {
        public string id; public string label; public string desc; public float chance;
    }

    [Serializable] public class QuestionDef
    {
        public string id; public string kind; public string text; public string[] targets; public int minutes;
        public string low; public string mid; public string high;
    }

    [Serializable] public class EventCfg
    {
        public float concealDiscoverChance; public float discloseOpinion; public float concealOpinion;
        public float lobbyCost; public float lobbyChancePerDay; public float ignoreLobbyOpinion;
        public float newsPerDay;
        public float raiseRequestDay;
    }

    [Serializable] public class NewsTemplate
    {
        public string kind; public string text; public string effect; public string axis; public float value;
    }

    [Serializable] public class LocationDef
    {
        public string id; public string label; public int phase; public int reactorSlots; public float rentPerDay;
    }
}
