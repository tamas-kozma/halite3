namespace Halite3.hlt
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    public sealed class OutboundMap
    {
        public TuningSettings TuningSettings { get; set; }
        public DataMapLayer<int> BaseHaliteMap { get; set; }
        public GameInitializationMessage GameInitializationMessage { get; set; }
        public TurnMessage TurnMessage { get; set; }
        public ReturnMap ReturnMap { get; set; }
        public Logger Logger { get; set; }

        public DataMapLayer<double> AdjustedHaliteMap { get; private set; }
        public DataMapLayer<double> DiscAverageLayer { get; private set; }
        public DataMapLayer<double> HarvestAreaMap { get; private set; }
        public DataMapLayer<double> OutboundPathCellValues { get; private set; }
        public DataMapLayer<int> OutboundPaths { get; private set; }

        public void Calculate()
        {
            CalculateAdjustedHaliteMap();
            CalculateHarvestAreaMap();
            CalculateOutboundPaths();
        }

        private void CalculateOutboundPaths()
        {
            int mapWidth = HarvestAreaMap.Width;
            int mapHeight = HarvestAreaMap.Height;
            OutboundPathCellValues = new DataMapLayer<double>(mapWidth, mapHeight);
            OutboundPaths = new DataMapLayer<int>(mapWidth, mapHeight);
            var queue = new PriorityQueue<double, PositionWithDistance>(HarvestAreaMap.CellCount);
            foreach (var position in HarvestAreaMap.AllPositions)
            {
                double value = HarvestAreaMap[position];
                var positionWithDistance = new PositionWithDistance(position, 0);
                queue.Enqueue(value, positionWithDistance);
            }
            return;
            var neighbourArray = new Position[4];
            double stepPenaltyMultiplier = TuningSettings.OutboundMapPathStepPenaltyMultiplier;
            while (queue.Count > 0)
            {
                double newValue = queue.PeekPriority();
                if (newValue == 0)
                {
                    continue;
                }

                var positionWithDistance = queue.Dequeue();
                double oldValue = OutboundPathCellValues[positionWithDistance.Position];
                if (newValue > oldValue)
                {
                    OutboundPaths[positionWithDistance.Position] = positionWithDistance.Distance;
                    OutboundPathCellValues[positionWithDistance.Position] = newValue;

                    double nextValue = newValue * stepPenaltyMultiplier;
                    int nextDistance = positionWithDistance.Distance + 1;
                    OutboundPathCellValues.GetNeighbours(positionWithDistance.Position, neighbourArray);
                    foreach (var neighbour in neighbourArray)
                    {
                        queue.Enqueue(nextValue, new PositionWithDistance(neighbour, nextDistance));
                    }
                }
            }
        }

        private void CalculateHarvestAreaMap()
        {
            int mapWidth = AdjustedHaliteMap.Width;
            int mapHeight = AdjustedHaliteMap.Height;
            DiscAverageLayer = new DataMapLayer<double>(mapWidth, mapHeight);

            int windowRadius = 2;
            Debug.Assert(AdjustedHaliteMap.Width > windowRadius * 2 + 1 && AdjustedHaliteMap.Height > windowRadius * 2 + 1);

            int discArea = AdjustedHaliteMap.GetDiscArea(windowRadius);
            var discPositions = new Position[discArea];
            for (int row = windowRadius; row < mapHeight - windowRadius; row++)
            {
                var rowHeadPosition = new Position(row, windowRadius);
                AdjustedHaliteMap.GetDiscCells(rowHeadPosition, windowRadius, discPositions);
                double discSum = discPositions.Sum(position => AdjustedHaliteMap[position]);
                DiscAverageLayer[rowHeadPosition] = discSum / discArea;

                for (int column = windowRadius + 1; column < mapWidth - windowRadius; column++)
                {
                    var position = new Position(row, column);
                    AdjustedHaliteMap.GetDiscCells(position, windowRadius, discPositions);
                    discSum = discPositions.Sum(discPosition => AdjustedHaliteMap[discPosition]);
                    /*
                    discSum -= AdjustedHaliteMap[new Position(row - 2, column - 1)];
                    discSum -= AdjustedHaliteMap[new Position(row - 1, column - 2)];
                    discSum -= AdjustedHaliteMap[new Position(row, column - 3)];
                    discSum -= AdjustedHaliteMap[new Position(row + 1, column - 2)];
                    discSum -= AdjustedHaliteMap[new Position(row + 2, column - 1)];

                    discSum += AdjustedHaliteMap[new Position(row - 2, column)];
                    discSum += AdjustedHaliteMap[new Position(row - 1, column + 1)];
                    discSum += AdjustedHaliteMap[new Position(row, column + 2)];
                    discSum += AdjustedHaliteMap[new Position(row + 1, column + 1)];
                    discSum += AdjustedHaliteMap[new Position(row + 2, column)];*/

                    DiscAverageLayer[new Position(row, column)] = discSum / discArea;
                }
            }

            for (int row = 0; row < mapHeight; row++)
            {
                bool isInnerRow = (row >= windowRadius && row < mapHeight - windowRadius);
                for (int column = 0; column < mapWidth; column++)
                {
                    if (isInnerRow && column == windowRadius)
                    {
                        column = mapWidth - windowRadius;
                    }

                    var position = new Position(row, column);
                    AdjustedHaliteMap.GetDiscCells(position, windowRadius, discPositions);
                    double discSum = discPositions.Sum(discPosition => AdjustedHaliteMap[discPosition]);
                    DiscAverageLayer[position] = discSum / discArea;
                }
            }

            double centerWeight = TuningSettings.OutboundMapHarvestAreaCenterWeight;
            HarvestAreaMap = new DataMapLayer<double>(mapWidth, mapHeight);
            foreach (var position in HarvestAreaMap.AllPositions)
            {
                double valueAtCell = AdjustedHaliteMap[position];
                double averageAtCell = DiscAverageLayer[position];
                HarvestAreaMap[position] = (valueAtCell * centerWeight + averageAtCell) / (centerWeight + 1);
            }
        }

        private void CalculateAdjustedHaliteMap()
        {
            AdjustedHaliteMap = new DataMapLayer<double>(BaseHaliteMap.Width, BaseHaliteMap.Height);
            foreach (var position in BaseHaliteMap.AllPositions)
            {
                int halite = BaseHaliteMap[position];
                int returnPathSumHalite = ReturnMap.CellData[position].SumHalite;
                double lostHalite = GameConstants.MoveCostRatio * returnPathSumHalite * TuningSettings.OutboundMapLostHaliteMultiplier;
                AdjustedHaliteMap[position] = Math.Max(halite - lostHalite, 0);
            }

            string myPlayerId = GameInitializationMessage.MyPlayerId;
            var opponentPlayerUpdateMessages = TurnMessage.PlayerUpdates
                .Where(message => message.PlayerId != myPlayerId);

            var opponentHarvesterPositions = opponentPlayerUpdateMessages
                .SelectMany(message => message.Ships)
                .Where(shipMessage =>
                    shipMessage.Halite > TuningSettings.OutboundMapMinOpponentHarvesterHalite
                    && shipMessage.Halite < TuningSettings.OutboundMapMaxOpponentHarvesterHalite)
                .Select(shipMessage => shipMessage.Position);

            AdjustHaliteInMultipleDiscs(opponentHarvesterPositions, TuningSettings.OutboundMapOpponentHarvesterBonusRadius, TuningSettings.OutboundMapOpponentHarvesterBonusMultiplier);

            var opponentPlayerIds = GameInitializationMessage.Players
                .Select(message => message.PlayerId)
                .Where(id => id != myPlayerId);

            var opponentDropoffPositions = opponentPlayerIds.SelectMany(playerId => 
                opponentPlayerUpdateMessages
                    .Single(message => message.PlayerId == playerId).Dropoffs
                    .Select(message => message.Position)
                .Concat(new Position[] {
                    GameInitializationMessage.Players
                        .Single(message => message.PlayerId == playerId).ShipyardPosition }));

            AdjustHaliteInMultipleDiscs(opponentDropoffPositions, TuningSettings.OutboundMapOpponentDropoffPenaltyRadius, TuningSettings.OutboundMapOpponentDropoffPenaltyMultiplier);
        }

        private void AdjustHaliteInMultipleDiscs(IEnumerable<Position> positions, int radius, double multiplier)
        {
            int discArea = BaseHaliteMap.GetDiscArea(radius);
            var discArray = new Position[discArea];
            foreach (var position in positions)
            {
                AdjustedHaliteMap.GetDiscCells(position, radius, discArray);
                foreach (var discPosition in discArray)
                {
                    double adjustedHalite = AdjustedHaliteMap[discPosition];
                    adjustedHalite *= multiplier;
                    AdjustedHaliteMap[discPosition] = adjustedHalite;
                }
            }
        }

        private struct PositionWithDistance
        {
            public readonly Position Position;
            public readonly int Distance;

            public PositionWithDistance(Position position, int distance)
            {
                Position = position;
                Distance = distance;
            }
        }
    }
}
