using System;

namespace Zarus.Systems
{
    [Serializable]
    public class ProvinceInfectionState
    {
        public string RegionId;
        public float Infection01;
        public int OutpostCount;
        public bool OutpostDisabled;
        public bool IsFullyInfected;

        public bool HasOutpost => OutpostCount > 0;
    }

    [Serializable]
    public class GlobalCureState
    {
        public float CureProgress01;
        public int ActiveOutpostCount;
        public int TotalOutpostCount;
        public int ZarBalance;
    }

    [Serializable]
    public struct OutpostCostConfig
    {
        public int BaseCostR;
        public int CostPerExistingOutpostR;
    }

    [Serializable]
    public struct OutpostRateConfig
    {
        public float LocalCurePerHour;
        public float GlobalCurePerHourPerOutpost;
        public float DiminishingReturnFactor;
        public float TargetWinDayMin;
        public float TargetWinDayMax;
    }

    [Serializable]
    public struct VirusRateConfig
    {
        public float BaseInfectionPerHour;
        public float DailyVirusGrowth;
        public float OutpostDisableThreshold01;
        public float FullyInfectedThreshold01;
    }

    public static class OutbreakMath
    {
        public static int ComputeOutpostCostR(int existingOutpostCount, OutpostCostConfig config)
        {
            if (existingOutpostCount < 0)
            {
                existingOutpostCount = 0;
            }

            return config.BaseCostR + config.CostPerExistingOutpostR * existingOutpostCount;
        }

        public static float ComputeGlobalOutpostMultiplierForIndex(int index, float diminishingFactor)
        {
            if (index <= 0)
            {
                return 1f;
            }

            float multiplier = 1f;
            for (int i = 0; i < index; i++)
            {
                multiplier *= diminishingFactor;
            }

            return multiplier;
        }
    }
}
