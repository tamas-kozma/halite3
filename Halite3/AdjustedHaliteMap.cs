namespace Halite3
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    public sealed class AdjustedHaliteMap
    {
        public TuningSettings TuningSettings { get; set; }
        public DataMapLayer<int> BaseHaliteMap { get; set; }
        public GameInitializationMessage GameInitializationMessage { get; set; }
        public TurnMessage TurnMessage { get; set; }
        public ReturnMap ReturnMap { get; set; }
        public Logger Logger { get; set; }
        public MapBooster MapBooster { get; set; }
        public BitMapLayer ForbiddenCellsMap { get; set; }
        public OpponentHarvestAreaMap OpponentHarvestAreaMap { get; set; }

        public DataMapLayer<double> ValuesMinusReturnCost { get; private set; }
        public DataMapLayer<double> Values;

        public void Calculate()
        {
            CalculateAdjustedHaliteMap();
        }

        private void CalculateAdjustedHaliteMap()
        {
            int mapWidth = BaseHaliteMap.Width;
            int mapHeight = BaseHaliteMap.Height;
            var valuesMinusReturnCost = new DataMapLayer<double>(mapWidth, mapHeight);
            ValuesMinusReturnCost = valuesMinusReturnCost;
            Values = new DataMapLayer<double>(mapWidth, mapHeight);
            double maxHalite = TuningSettings.AdjustedHaliteMapMaxHalite;
            var opponentHarvestAreaBonusMultiplierMap = OpponentHarvestAreaMap.HaliteMultiplierMap;
            foreach (var position in BaseHaliteMap.AllPositions)
            {
                if (ForbiddenCellsMap[position])
                {
                    valuesMinusReturnCost[position] = 0;
                    continue;
                }

                double adjustedHalite = BaseHaliteMap[position];
                double opponentHarvestAreaBonusMultiplier = opponentHarvestAreaBonusMultiplierMap[position];
                Debug.Assert(opponentHarvestAreaBonusMultiplier == 0 || (opponentHarvestAreaBonusMultiplier >= 1d && opponentHarvestAreaBonusMultiplier <= GameConstants.InspiredBonusMultiplier));
                if (opponentHarvestAreaBonusMultiplier != 0)
                {
                    adjustedHalite *= opponentHarvestAreaBonusMultiplier;
                }

                Values[position] = Math.Min(maxHalite, adjustedHalite);

                int returnPathSumHalite = ReturnMap.CellData[position].SumHalite;
                double lostHalite = GameConstants.MoveCostRatio * returnPathSumHalite * TuningSettings.AdjustedHaliteMapLostHaliteMultiplier;
                adjustedHalite = Math.Max(adjustedHalite - lostHalite, 0);
                adjustedHalite = Math.Min(maxHalite, adjustedHalite);
                valuesMinusReturnCost[position] = adjustedHalite;
            }
        }
    }
}
