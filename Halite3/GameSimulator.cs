namespace Halite3
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;

    public sealed class GameSimulator
    {
        public TuningSettings TuningSettings;
        public DataMapLayer<int> HaliteMap;
        public MyPlayer MyPlayer;
        public OpponentPlayer[] Opponents;
        public Logger Logger;
        public MapBooster MapBooster;
        public int TurnNumber;
        public int TotalTurns;

        public Player[] AllPlayers;
        public List<Position>[][] AllPlayerDropoffDistancePositionLists;

        public void RunSimulation()
        {
            int playerCount = 1 + Opponents.Length;
            AllPlayers = new Player[playerCount];
            AllPlayers[0] = MyPlayer;
            Opponents.CopyTo(AllPlayers, 1);

            AllPlayerDropoffDistancePositionLists = new List<Position>[playerCount][];
            int maxDistance = (HaliteMap.Width + HaliteMap.Height) / 2;
            for (int playerIndex = 0; playerIndex < playerCount; playerIndex++)
            {
                var player = AllPlayers[playerIndex];
                var distanceLists = new List<Position>[maxDistance];
                AllPlayerDropoffDistancePositionLists[playerIndex] = distanceLists;
                for (int distance = 0; distance < maxDistance; distance++)
                {
                    int maxPositionCount = HaliteMap.GetCircleCircumFerence(distance * player.Dropoffs.Count);
                    distanceLists[distance] = new List<Position>(maxPositionCount);
                }

                var distanceMap = player.DistanceFromDropoffMap;
                foreach (var position in HaliteMap.AllPositions)
                {
                    int distance = distanceMap[position];
                    distanceLists[distance].Add(position);
                }
            }

            double remainingTimeRatio = (TotalTurns - TurnNumber) / (double)TotalTurns;
            var visitedCells = new BitMapLayer(HaliteMap.Width, HaliteMap.Height);

        }
    }
}
