namespace Halite3
{
    using System.Diagnostics;
    using System.Linq;

    public sealed class ExpansionMap
    {
        private static readonly int CoarseCellSize = 8;

        public TuningSettings TuningSettings;
        public DataMapLayer<int> HaliteMap;
        public MyPlayer MyPlayer;
        public OpponentPlayer[] Opponents;
        public Logger Logger;
        public MapBooster MapBooster;
        public BitMapLayer ForbiddenCellsMap;

        public Position[] AllDropoffPositions;
        public DataMapLayer<int>[] CoarseHaliteMaps;
        public DataMapLayer<int>[] CoarseShipCountMaps;
        public DataMapLayer<double> Paths;
        public bool SuitableLocationExists;
        public Position BestDropoffPosition;

        public void Calculate()
        {
            CalculateCoarseHaliteMaps();
            CalculateCoarseShipCountMaps();
            CalculatePaths();
        }

        public void CalculatePaths()
        {
            AllDropoffPositions = MyPlayer.Dropoffs
                .Concat(Opponents.SelectMany(opponent => opponent.Dropoffs))
                .Select(dropoff => dropoff.Position)
                .ToArray();

            int maxCoarseHalite = int.MinValue;
            var maxHaliteCoarsePosition = default(Position);
            int maxCoarseHaliteIndex = 0;
            int maxBaseOffset = 0;
            var coarseDisc = new Position[CoarseHaliteMaps[0].GetDiscArea(1)];
            for (int i = 0; i < 2; i++)
            {
                int baseOffset = i * (CoarseCellSize / 2);
                var coarseHaliteMap = CoarseHaliteMaps[i];
                var coarseShipCountMap = CoarseShipCountMaps[i];
                foreach (var coarsePosition in coarseHaliteMap.AllPositions)
                {
                    int halite = coarseHaliteMap[coarsePosition];
                    if (halite <= maxCoarseHalite)
                    {
                        continue;
                    }

                    int sumShipCount = 0;
                    coarseHaliteMap.GetDiscCells(coarsePosition, 1, coarseDisc);
                    foreach (var coarseDiscPosition in coarseDisc)
                    {
                        sumShipCount += coarseShipCountMap[coarseDiscPosition];
                    }

                    if (sumShipCount == 0)
                    {
                        continue;
                    }

                    int centerRow = HaliteMap.NormalizeNonNegativeRow((coarsePosition.Row * CoarseCellSize) + (CoarseCellSize / 2) + baseOffset);
                    int centerColumn = HaliteMap.NormalizeNonNegativeColumn((coarsePosition.Column * CoarseCellSize) + (CoarseCellSize / 2) + baseOffset);
                    var center = new Position(centerRow, centerColumn);
                    if (IsTooCloseToDropoff(center))
                    {
                        continue;
                    }

                    maxCoarseHalite = halite;
                    maxHaliteCoarsePosition = coarsePosition;
                    maxCoarseHaliteIndex = i;
                    maxBaseOffset = baseOffset;
                }
            }

            Paths = new DataMapLayer<double>(MapBooster.MapWidth, MapBooster.MapHeight);
            SuitableLocationExists = (maxCoarseHalite > 0);
            if (!SuitableLocationExists)
            {
                return;
            }

            var queue = new DoublePriorityQueue<Position>(HaliteMap.CellCount);
            int rowOffset = maxBaseOffset + maxHaliteCoarsePosition.Row * CoarseCellSize;
            int columnOffset = maxBaseOffset + maxHaliteCoarsePosition.Column * CoarseCellSize;
            for (int cellRow = 0; cellRow < CoarseCellSize; cellRow++)
            {
                int row = HaliteMap.NormalizeNonNegativeRow(rowOffset + cellRow);
                for (int cellColumn = 0; cellColumn < CoarseCellSize; cellColumn++)
                {
                    int column = HaliteMap.NormalizeNonNegativeRow(columnOffset + cellColumn);
                    var position = new Position(row, column);
                    if (IsTooCloseToDropoff(position))
                    {
                        continue;
                    }

                    int halite = HaliteMap[position];
                    queue.Enqueue(-1 * halite, position);
                }
            }

            SuitableLocationExists = (queue.Count > 0);
            if (SuitableLocationExists)
            {
                BestDropoffPosition = queue.Peek();
            }

            while (queue.Count > 0)
            {
                double halite = -1 * queue.PeekPriority();
                var position = queue.Dequeue();
                if (ForbiddenCellsMap[position])
                {
                    continue;
                }

                if (Paths[position] >= halite)
                {
                    continue;
                }

                Paths[position] = halite;
                double nextHalite = halite * 0.8d;
                foreach (var neighbour in MapBooster.GetNeighbours(position))
                {
                    if (Paths[neighbour] >= nextHalite
                        || ForbiddenCellsMap[neighbour])
                    {
                        continue;
                    }

                    queue.Enqueue(-1 * nextHalite, neighbour);
                }
            }
        }

        private bool IsTooCloseToDropoff(Position position)
        {
            foreach (var dropoffPosition in AllDropoffPositions)
            {
                int distance = MapBooster.Distance(dropoffPosition, position);
                if (distance < CoarseCellSize * 2)
                {
                    return true;
                }
            }

            return false;
        }

        public void CalculateCoarseShipCountMaps()
        {
            int mapWidth = MapBooster.MapWidth;
            int mapHeight = MapBooster.MapHeight;
            Debug.Assert(mapWidth % CoarseCellSize == 0 && mapHeight % CoarseCellSize == 0);
            int coarseMapWidth = mapWidth / CoarseCellSize;
            int coarseMapHeight = mapHeight / CoarseCellSize;
            CoarseShipCountMaps = new DataMapLayer<int>[2];
            for (int i = 0; i < 2; i++)
            {
                var shipCountMap = new DataMapLayer<int>(coarseMapWidth, coarseMapHeight);
                CoarseShipCountMaps[i] = shipCountMap;
                int baseOffset = i * (CoarseCellSize / 2);
                foreach (var ship in MyPlayer.Ships)
                {
                    int coarseRow = HaliteMap.NormalizeSingleNegativeRow(ship.Position.Row - baseOffset) / CoarseCellSize;
                    int coarseColumn = HaliteMap.NormalizeSingleNegativeColumn(ship.Position.Column - baseOffset) / CoarseCellSize;
                    var coarsePosition = new Position(coarseRow, coarseColumn);
                    shipCountMap[coarsePosition]++;
                }
            }
        }

        public void CalculateCoarseHaliteMaps()
        {
            int mapWidth = MapBooster.MapWidth;
            int mapHeight = MapBooster.MapHeight;
            Debug.Assert(mapWidth % CoarseCellSize == 0 && mapHeight % CoarseCellSize == 0);
            int coarseMapWidth = mapWidth / CoarseCellSize;
            int coarseMapHeight = mapHeight / CoarseCellSize;
            CoarseHaliteMaps = new DataMapLayer<int>[2];
            for (int i = 0; i < 2; i++)
            {
                int baseOffset = i * (CoarseCellSize / 2);
                var coarseHaliteMap = new DataMapLayer<int>(coarseMapWidth, coarseMapHeight);
                CoarseHaliteMaps[i] = coarseHaliteMap;
                foreach (var coarsePosition in coarseHaliteMap.AllPositions)
                {
                    int sumHalite = 0;
                    for (int cellRow = 0; cellRow < CoarseCellSize; cellRow++)
                    {
                        int rowOffset = baseOffset + coarsePosition.Row * CoarseCellSize;
                        int row = HaliteMap.NormalizeNonNegativeRow(rowOffset + cellRow);
                        for (int cellColumn = 0; cellColumn < CoarseCellSize; cellColumn++)
                        {
                            int columnOffset = baseOffset + coarsePosition.Column * CoarseCellSize;
                            int column = HaliteMap.NormalizeNonNegativeColumn(columnOffset + cellColumn);
                            sumHalite += HaliteMap[row, column];
                        }
                    }

                    coarseHaliteMap[coarsePosition] = sumHalite;
                }
            }
        }
    }
}
