namespace Halite3
{
    using System;
    using System.Collections.Generic;
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

        public DataMapLayer<double> Values { get; private set; }

        public void Calculate()
        {
            CalculateAdjustedHaliteMap();
        }

        private void CalculateAdjustedHaliteMap()
        {
            Values = new DataMapLayer<double>(BaseHaliteMap.Width, BaseHaliteMap.Height);
            double maxHalite = TuningSettings.AdjustedHaliteMapMaxHalite;
            foreach (var position in BaseHaliteMap.AllPositions)
            {
                if (ForbiddenCellsMap[position])
                {
                    Values[position] = 0;
                    continue;
                }

                int halite = BaseHaliteMap[position];
                int returnPathSumHalite = ReturnMap.CellData[position].SumHalite;
                double lostHalite = GameConstants.MoveCostRatio * returnPathSumHalite * TuningSettings.AdjustedHaliteMapLostHaliteMultiplier;
                Values[position] = Math.Min(maxHalite, Math.Max(halite - lostHalite, 0));
            }

            string myPlayerId = GameInitializationMessage.MyPlayerId;
            var opponentPlayerUpdateMessages = TurnMessage.PlayerUpdates
                .Where(message => message.PlayerId != myPlayerId);

            // TODO: Replace this with a more permanent smell based bonus, to reduce destination fluctuation.
            var opponentHarvesterPositions = opponentPlayerUpdateMessages
                .SelectMany(message => message.Ships)
                .Where(shipMessage =>
                    shipMessage.Halite > TuningSettings.OutboundMapMinOpponentHarvesterHalite
                    && shipMessage.Halite < TuningSettings.OutboundMapMaxOpponentHarvesterHalite)
                .Select(shipMessage => shipMessage.Position);

            AdjustHaliteInMultipleDiscs(opponentHarvesterPositions, TuningSettings.OutboundMapOpponentHarvesterBonusRadius, TuningSettings.OutboundMapOpponentHarvesterBonusMultiplier);
        }

        private void AdjustHaliteInMultipleDiscs(IEnumerable<Position> positions, int radius, double multiplier)
        {
            int discArea = BaseHaliteMap.GetDiscArea(radius);
            var discArray = new Position[discArea];
            foreach (var position in positions)
            {
                Values.GetDiscCells(position, radius, discArray);
                foreach (var discPosition in discArray)
                {
                    double adjustedHalite = Values[discPosition];
                    adjustedHalite *= multiplier;
                    Values[discPosition] = adjustedHalite;
                }
            }
        }
    }
}
