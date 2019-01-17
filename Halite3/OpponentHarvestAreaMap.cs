namespace Halite3
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    public sealed class OpponentHarvestAreaMap
    {
        public TuningSettings TuningSettings;
        public Logger Logger;
        public MapBooster MapBooster;
        public DataMapLayer<Ship> AllOpponentShipMap;

        public readonly Dictionary<Position, ScentInfo> HarvestAreaCenters;
        public readonly DataMapLayer<double> HaliteMultiplierMap;

        public OpponentHarvestAreaMap(MapBooster mapBooster)
        {
            MapBooster = mapBooster;

            HarvestAreaCenters = new Dictionary<Position, ScentInfo>();
            HaliteMultiplierMap = new DataMapLayer<double>(MapBooster.MapWidth, MapBooster.MapHeight);
        }

        public void Update(List<Position> opponentHarvestPositionList)
        {
            Logger.LogDebug("Updating OpponentHarvestAreaMap with positions " + string.Join(", ", opponentHarvestPositionList));

            var oldPairs = HarvestAreaCenters.ToArray();
            HarvestAreaCenters.Clear();
            foreach (var pair in oldPairs)
            {
                var scent = pair.Value;
                if (scent.Strength == 1)
                {
                    continue;
                }

                scent = new ScentInfo(scent.Strength - 1, scent.Multiplier);
                HarvestAreaCenters.Add(pair.Key, scent);
            }

            var mapCalculator = MapBooster.Calculator;
            var neighbourhoodDisc = new Position[mapCalculator.GetDiscArea(1)];
            int maxScentStrength = TuningSettings.OpponentHarvestAreaMapMaxScentStrength;
            Debug.Assert(maxScentStrength > 0);
            foreach (var harvestPosition in opponentHarvestPositionList)
            {
                var ship = AllOpponentShipMap[harvestPosition];
                Debug.Assert(ship != null);
                if (ship.Halite < TuningSettings.OpponentShipLikelyHarvesterMinHalite)
                {
                    continue;
                }

                int oldMultiplier = 0;
                mapCalculator.GetDiscCells(harvestPosition, 1, neighbourhoodDisc);
                foreach (var discPosition in neighbourhoodDisc)
                {
                    if (!HarvestAreaCenters.Remove(discPosition, out var oldScent))
                    {
                        continue;
                    }

                    oldMultiplier += oldScent.Multiplier;
                }

                int mutiplier = Math.Max(1, oldMultiplier);
                var newScent = new ScentInfo(maxScentStrength, mutiplier);
                HarvestAreaCenters.Add(harvestPosition, newScent);
            }

            HaliteMultiplierMap.Clear();
            if (GameConstants.InspirationRadius <= 2 || GameConstants.InspirationRadius > MapBooster.MapWidth / 2)
            {
                return;
            }

            Debug.Assert(TuningSettings.OpponentHarvestAreaMapHaliteBonusMultiplier >= 1);
            double baseBonusMultiplierMinusOne = TuningSettings.OpponentHarvestAreaMapHaliteBonusMultiplier - 1;
            int bonusRadius = GameConstants.InspirationRadius + TuningSettings.OpponentHarvestAreaMapHaliteBonusExtraRadius;
            var bonusDisc = new Position[mapCalculator.GetDiscArea(bonusRadius)];
            double bonusMultiplierCap = TuningSettings.OpponentHarvestAreaMapHaliteBonusMultiplierCap;
            foreach (var pair in HarvestAreaCenters)
            {
                var scent = pair.Value;
                Debug.Assert(scent.Strength > 0 && scent.Strength <= maxScentStrength);
                double strengthRatio = scent.Strength / (double)maxScentStrength;
                double singleBonusMultiplier = (baseBonusMultiplierMinusOne * strengthRatio) + 1;
                double bonusMultiplier = Math.Pow(singleBonusMultiplier, scent.Multiplier);
                Debug.Assert(bonusMultiplier >= 1d);

                var center = pair.Key;
                mapCalculator.GetDiscCells(center, bonusRadius, bonusDisc);
                foreach (var position in bonusDisc)
                {
                    double existingBonusMultiplier = HaliteMultiplierMap[position];
                    Debug.Assert(existingBonusMultiplier == 0 || existingBonusMultiplier >= 1d);
                    double newBonusMultiplier = (existingBonusMultiplier == 0) ? bonusMultiplier : existingBonusMultiplier * bonusMultiplier;
                    newBonusMultiplier = Math.Min(newBonusMultiplier, bonusMultiplierCap);
                    HaliteMultiplierMap[position] = newBonusMultiplier;
                }
            }
        }

        public struct ScentInfo
        {
            public readonly int Strength;
            public readonly int Multiplier;

            public ScentInfo(int strength, int multiplier)
            {
                Strength = strength;
                Multiplier = multiplier;
            }
        }
    }
}
