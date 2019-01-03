﻿namespace Halite3.hlt
{
    using System;
    using System.Diagnostics;
    using System.Linq;

    public sealed class ReturnMap
    {
        private Position[] dropoffPositions;

        public TuningSettings TuningSettings { get; set; }
        public DataMapLayer<int> HaliteMap { get; set; }
        public PlayerInitializationMessage MyPlayerInitializationMessage { get; set; }
        public PlayerUpdateMessage MyPlayerUpdateMessage { get; set; }
        public Logger Logger { get; set; }

        public DataMapLayer<double> PathCosts { get; private set; }
        public DataMapLayer<ReturnMapCellData> CellData { get; private set; }

        public void Calculate()
        {
            dropoffPositions = new Position[MyPlayerUpdateMessage.Dropoffs.Length + 1];
            dropoffPositions[0] = MyPlayerInitializationMessage.ShipyardPosition;
            for (int i = 0; i < MyPlayerUpdateMessage.Dropoffs.Length; i++)
            {
                dropoffPositions[i + 1] = MyPlayerUpdateMessage.Dropoffs[i].Position;
            }

            PathCosts = new DataMapLayer<double>(HaliteMap.Width, HaliteMap.Height);
            PathCosts.Fill(double.MaxValue);
            CellData = new DataMapLayer<ReturnMapCellData>(HaliteMap.Width, HaliteMap.Height);

            var queue = new PriorityQueue<double, Position>();
            foreach (var position in dropoffPositions)
            {
                PathCosts[position] = 0d;
                CellData[position] = new ReturnMapCellData(0, 0);
                queue.Enqueue(0d, position);
            }

            var neighbours = new Position[4];
            while (queue.Count > 0)
            {
                var position = queue.Dequeue();
                var cellData = CellData[position];
                HaliteMap.GetNeighbours(position, neighbours);
                foreach (var neighbour in neighbours)
                {
                    double oldNeighbourCost = PathCosts[neighbour];
                    int neighbourHalite = HaliteMap[neighbour];
                    var newNeighbourCellData = new ReturnMapCellData(cellData.Distance + 1, cellData.SumHalite + neighbourHalite);
                    double newNeighbourCost = GetPathCost(neighbour, newNeighbourCellData);
                    if (oldNeighbourCost <= newNeighbourCost)
                    {
                        continue;
                    }

                    CellData[neighbour] = newNeighbourCellData;
                    PathCosts[neighbour] = newNeighbourCost;
                    queue.Enqueue(newNeighbourCost, neighbour);
                }
            }
        }

        private double GetPathCost(Position pathStartPosition, ReturnMapCellData data)
        {
            int directDistance = GetDirectDistanceFromDropoff(pathStartPosition);
            Debug.Assert(data.Distance >= directDistance);
            double distanceRatio = data.Distance / (double)directDistance;
            double multiplier = ((distanceRatio - 1) * TuningSettings.ReturnPathDistancePenaltyMultiplier) + 1;
            return data.SumHalite * multiplier;
        }

        private int GetDirectDistanceFromDropoff(Position position)
        {
            return dropoffPositions
                .Select(dropoffPosition => HaliteMap.WraparoundDistance(position, dropoffPosition))
                .Min();
        }
    }
}