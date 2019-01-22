namespace Halite3
{
    using System.Collections.Generic;
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

        public List<Position> AllOpponentDropoffPositions;
        public List<Position> AllMyDropoffPositions;
        public DataMapLayer<int>[] CoarseHaliteMaps;
        public BitMapLayer[] CoarseShipVisitsMaps;
        public DataMapLayer<double> Paths;
        public List<DropoffAreaInfo> BestDropoffAreaCandidates;

        public void Initialize()
        {
            BestDropoffAreaCandidates = new List<DropoffAreaInfo>();

            int mapWidth = MapBooster.MapWidth;
            int mapHeight = MapBooster.MapHeight;
            Debug.Assert(mapWidth % CoarseCellSize == 0 && mapHeight % CoarseCellSize == 0);
            int coarseMapWidth = mapWidth / CoarseCellSize;
            int coarseMapHeight = mapHeight / CoarseCellSize;
            CoarseShipVisitsMaps = new BitMapLayer[2];
            for (int i = 0; i < 2; i++)
            {
                CoarseShipVisitsMaps[i] = new BitMapLayer(coarseMapWidth, coarseMapHeight);
            }
        }

        public void FindBestCandidates(BitMapLayer forbiddenCellsMap)
        {
            ForbiddenCellsMap = forbiddenCellsMap;
            BestDropoffAreaCandidates.Clear();

            CalculateCoarseHaliteMaps();
            UpdateCoarseShipVisitsMaps();
            CalculateCandidates();
        }

        public bool CalculatePaths(DropoffAreaInfo targetArea)
        {
            ResetDropoffPosition();
            Paths = new DataMapLayer<double>(MapBooster.MapWidth, MapBooster.MapHeight);
            var queue = new DoublePriorityQueue<Position>(HaliteMap.CellCount);
            int rowOffset = targetArea.BaseOffset + targetArea.CoarsePosition.Row * CoarseCellSize;
            int columnOffset = targetArea.BaseOffset + targetArea.CoarsePosition.Column * CoarseCellSize;
            for (int cellRow = 0; cellRow < CoarseCellSize; cellRow++)
            {
                int row = HaliteMap.NormalizeNonNegativeRow(rowOffset + cellRow);
                for (int cellColumn = 0; cellColumn < CoarseCellSize; cellColumn++)
                {
                    int column = HaliteMap.NormalizeNonNegativeRow(columnOffset + cellColumn);
                    var position = new Position(row, column);
                    if (ForbiddenCellsMap[position] 
                        || IsTooCloseToDropoff(position))
                    {
                        continue;
                    }

                    bool forbiddenNeighbourFound = false;
                    foreach (var neighbour in MapBooster.GetNeighbours(position))
                    {
                        if (ForbiddenCellsMap[neighbour])
                        {
                            forbiddenNeighbourFound = true;
                            break;
                        }
                    }

                    if (forbiddenNeighbourFound)
                    {
                        continue;
                    }

                    int halite = HaliteMap[position];
                    queue.Enqueue(-1 * halite, position);
                }
            }

            if (queue.Count == 0)
            {
                return false;
            }

            int visitedCellCount = 0;
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
                visitedCellCount++;
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

            return visitedCellCount > CoarseCellSize * CoarseCellSize * 9;
        }

        public sealed class DropoffAreaInfo
        {
            public Position CenterPosition;
            public Position CoarsePosition;
            internal int Index;
            internal int BaseOffset;
        }

        private void ResetDropoffPosition()
        {
            AllMyDropoffPositions = MyPlayer.Dropoffs.Select(dropoff => dropoff.Position).ToList();
            AllOpponentDropoffPositions = Opponents.SelectMany(opponent => opponent.Dropoffs).Select(dropoff => dropoff.Position).ToList();
        }

        private void CalculateCandidates()
        {
            ResetDropoffPosition();

            while (true)
            {
                int maxCoarseHalite = int.MinValue;
                var maxHaliteCoarsePosition = default(Position);
                int maxCoarseHaliteIndex = 0;
                int maxBaseOffset = 0;
                Position maxDropoffAreaCenterPosition = default(Position);
                var coarseDisc = new Position[CoarseHaliteMaps[0].GetDiscArea(2)];
                for (int i = 0; i < 2; i++)
                {
                    int baseOffset = i * (CoarseCellSize / 2);
                    var coarseHaliteMap = CoarseHaliteMaps[i];
                    var coarseShipCountMap = CoarseShipVisitsMaps[i];
                    foreach (var coarsePosition in coarseHaliteMap.AllPositions)
                    {
                        int halite = coarseHaliteMap[coarsePosition];
                        if (halite <= maxCoarseHalite)
                        {
                            continue;
                        }

                        bool hasShipBeenClose = false;//coarseShipCountMap[coarsePosition];
                        coarseHaliteMap.GetDiscCells(coarsePosition, 2, coarseDisc);
                        foreach (var coarseDiscPosition in coarseDisc)
                        {
                            if (coarseHaliteMap.MaxSingleDimensionDistance(coarsePosition, coarseDiscPosition) == 2)
                            {
                                continue;
                            }

                            hasShipBeenClose |= coarseShipCountMap[coarseDiscPosition];
                        }

                        if (!hasShipBeenClose)
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
                        maxDropoffAreaCenterPosition = center;
                    }
                }

                if (maxCoarseHalite <= 0)
                {
                    Logger.LogDebug("Expansion candidate count = " + BestDropoffAreaCandidates.Count);
                    return;
                }

                var areaInfo = new DropoffAreaInfo()
                {
                    BaseOffset = maxBaseOffset,
                    Index = maxCoarseHaliteIndex,
                    CenterPosition = maxDropoffAreaCenterPosition,
                    CoarsePosition = maxHaliteCoarsePosition
                };

                BestDropoffAreaCandidates.Add(areaInfo);
                AllMyDropoffPositions.Add(areaInfo.CenterPosition);
            }
        }

        private bool IsTooCloseToDropoff(Position position)
        {
            foreach (var dropoffPosition in AllMyDropoffPositions)
            {
                int distance = MapBooster.Distance(dropoffPosition, position);
                if (distance < 12)
                {
                    return true;
                }
            }

            foreach (var dropoffPosition in AllOpponentDropoffPositions)
            {
                int distance = MapBooster.Distance(dropoffPosition, position);
                if (distance < 8)
                {
                    return true;
                }
            }

            return false;
        }

        public void UpdateCoarseShipVisitsMaps()
        {
            for (int i = 0; i < 2; i++)
            {
                var shipCountMap = CoarseShipVisitsMaps[i];
                int baseOffset = i * (CoarseCellSize / 2);
                foreach (var ship in MyPlayer.Ships)
                {
                    int coarseRow = HaliteMap.NormalizeSingleNegativeRow(ship.Position.Row - baseOffset) / CoarseCellSize;
                    int coarseColumn = HaliteMap.NormalizeSingleNegativeColumn(ship.Position.Column - baseOffset) / CoarseCellSize;
                    var coarsePosition = new Position(coarseRow, coarseColumn);
                    shipCountMap[coarsePosition] = true;
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

            var neighbours = new Position[4];
            for (int i = 0; i < 2; i++)
            {
                int baseOffset = i * (CoarseCellSize / 2);
                var coarseHaliteMap = CoarseHaliteMaps[i];
                var coarseHaliteMapCopy = new DataMapLayer<int>(coarseHaliteMap);
                foreach (var coarsePosition in coarseHaliteMapCopy.AllPositions)
                {
                    int sumNeighbourHalite = 0;
                    coarseHaliteMapCopy.GetNeighbours(coarsePosition, neighbours);
                    foreach (var coarseNeighbour in neighbours)
                    {
                        sumNeighbourHalite += coarseHaliteMapCopy[coarseNeighbour];
                    }

                    int originHalite = coarseHaliteMap[coarsePosition];
                    int averageHalite = (sumNeighbourHalite / 4 + originHalite) / 2;
                    coarseHaliteMap[coarsePosition] = averageHalite;
                }
            }
        }
    }
}
