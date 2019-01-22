namespace Halite3
{
    using System.Linq;

    public sealed class LemmingMap
    {
        public TuningSettings TuningSettings;
        public Logger Logger;
        public MyPlayer MyPlayer;
        public MapBooster MapBooster;
        public BitMapLayer ForbiddenCellsMap;

        public Position[] DropoffPositions;
        public DataMapLayer<double> Paths;

        public void Calculate()
        {
            DropoffPositions = MyPlayer.Dropoffs.Select(dropoff => dropoff.Position).ToArray();
            var distanceFromDropoffMap = MyPlayer.DistanceFromDropoffMap;

            Paths = new DataMapLayer<double>(distanceFromDropoffMap.Width, distanceFromDropoffMap.Height);
            Paths.Fill(double.MaxValue);

            var queue = new DoublePriorityQueue<Position>();
            foreach (var position in DropoffPositions)
            {
                Paths[position] = 0d;
                queue.Enqueue(0d, position);
            }

            while (queue.Count > 0)
            {
                double distance = queue.PeekPriority();
                var position = queue.Dequeue();
                var neighbours = MapBooster.GetNeighbours(position);
                foreach (var neighbour in neighbours)
                {
                    if (ForbiddenCellsMap[neighbour])
                    {
                        continue;
                    }

                    double nextDistance = distance + 1;
                    double oldNeighbourDistance = Paths[neighbour];
                    if (oldNeighbourDistance <= nextDistance)
                    {
                        continue;
                    }

                    Paths[neighbour] = nextDistance;
                    queue.Enqueue(nextDistance, neighbour);
                }
            }
        }
    }
}
