namespace Halite3
{
    using System.Diagnostics;
    using System.Linq;

    public sealed class ReturnMap
    {
        private Position[] dropoffPositions;

        public TuningSettings TuningSettings { get; set; }
        public DataMapLayer<int> HaliteMap { get; set; }
        public MyPlayer MyPlayer { get; set; }
        public Logger Logger { get; set; }
        public MapBooster MapBooster { get; set; }
        public BitMapLayer ForbiddenCellsMap { get; set; }

        public DataMapLayer<double> PathCosts { get; private set; }
        public DataMapLayer<ReturnMapCellData> CellData { get; private set; }

        private DataMapLayer<int> distanceFromDropoffMap;

        public void Calculate()
        {
            dropoffPositions = MyPlayer.Dropoffs.Select(dropoff => dropoff.Position).ToArray();
            distanceFromDropoffMap = MyPlayer.DistanceFromDropoffMap;

            var pathCosts = new DataMapLayer<double>(HaliteMap.Width, HaliteMap.Height);
            PathCosts = pathCosts;
            pathCosts.Fill(double.MaxValue);
            var cellDataMap = new DataMapLayer<ReturnMapCellData>(HaliteMap.Width, HaliteMap.Height);
            CellData = cellDataMap;

            var queue = new DoublePriorityQueue<Position>();
            foreach (var position in dropoffPositions)
            {
                pathCosts[position] = 0d;
                cellDataMap[position] = new ReturnMapCellData(0, 0);
                queue.Enqueue(0d, position);
            }

            var mapBooster = MapBooster;
            var forbiddenCellsMap = ForbiddenCellsMap;
            var forbiddenCellData = new ReturnMapCellData(int.MaxValue, int.MaxValue);
            var haliteMap = HaliteMap;
            while (queue.Count > 0)
            {
                var position = queue.Dequeue();
                var cellData = cellDataMap[position];
                var neighbours = mapBooster.GetNeighbours(position.Row, position.Column);
                foreach (var neighbour in neighbours)
                {
                    if (forbiddenCellsMap[neighbour])
                    {
                        cellDataMap[neighbour] = forbiddenCellData;
                        continue;
                    }

                    double oldNeighbourCost = pathCosts[neighbour];

                    // Adding plus one to ensure that path values strictly increase as we move away from a dropoff.
                    int neighbourHalite = haliteMap[neighbour] + 1;
                    var newNeighbourCellData = new ReturnMapCellData(cellData.Distance + 1, cellData.SumHalite + neighbourHalite);
                    double newNeighbourCost = GetPathCost(neighbour, newNeighbourCellData);
                    if (oldNeighbourCost <= newNeighbourCost)
                    {
                        continue;
                    }

                    cellDataMap[neighbour] = newNeighbourCellData;
                    pathCosts[neighbour] = newNeighbourCost;
                    queue.Enqueue(newNeighbourCost, neighbour);
                }
            }
        }

        private double GetPathCost(Position pathStartPosition, ReturnMapCellData data)
        {
            int directDistance = distanceFromDropoffMap[pathStartPosition];
            if (directDistance == 0)
            {
                return double.MaxValue;
            }

            Debug.Assert(data.Distance >= directDistance);
            double distanceRatio = data.Distance / (double)directDistance;
            double multiplier = ((distanceRatio - 1) * TuningSettings.ReturnPathDistancePenaltyMultiplier) + 1;
            return data.SumHalite * multiplier;
        }
    }
}
