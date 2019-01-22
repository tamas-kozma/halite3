namespace Halite3
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;

    public sealed class LemmingDisease
    {
        public TuningSettings TuningSettings;
        public Logger Logger;
        public MyPlayer MyPlayer;
        public int TotalTurns;
        public Action<MyShip, ShipRole> SetShipRole;
        public Func<LemmingMap> GetLemmingMap;

        public LemmingMap LemmingMap;
        public int TurnNumber;
        public List<MyShip> ShipsSorted;
        public int FrontDistance;

        public void Initialize()
        {
            ShipsSorted = new List<MyShip>();
        }

        public void Infect(int turnNumber)
        {
            TurnNumber = turnNumber;
            int turnsRemaining = TotalTurns - TurnNumber;
            LemmingMap = GetLemmingMap();
            var lemmingPaths = LemmingMap.Paths;

            ShipsSorted.Clear();
            ShipsSorted.AddRange(MyPlayer.MyShips);
            foreach (var ship in ShipsSorted)
            {
                ship.LemmingMapPathDistance = lemmingPaths[ship.OriginPosition];
            }

            ShipsSorted.Sort(ShipLemmingDistanceReverseComparer.Default);

            FrontDistance = int.MaxValue;
            int effectiveDropoffCount = Math.Max(1, MyPlayer.Dropoffs.Count - 1);
            double lemmingDropoffTurnCapacity = TuningSettings.LemmingDropoffTurnCapacity * effectiveDropoffCount;
            int index = 0;
            int outerShipCount = 0;
            while (index < ShipsSorted.Count)
            {
                var currentShip = ShipsSorted[index];
                int distance = GetSafeLemmingDistance(currentShip);
                int startIndex = index;
                int immuneCountAtDistance = 0;
                while (index < ShipsSorted.Count && GetSafeLemmingDistance(currentShip) == distance)
                {
                    if (IsImmune(currentShip))
                    {
                        immuneCountAtDistance++;
                    }

                    index++;
                    if (index < ShipsSorted.Count)
                    {
                        currentShip = ShipsSorted[index];
                    }
                }

                int shipCountAtDistance = (index - startIndex) - immuneCountAtDistance;
                int extraTurnsNeeded = (int)Math.Ceiling((shipCountAtDistance + outerShipCount) / lemmingDropoffTurnCapacity);
                int totalTurnsNeeded = distance + extraTurnsNeeded;
                int groupCount = Math.Max(totalTurnsNeeded + 1 - turnsRemaining, 0);
                int neededLemmingCount = (int)Math.Ceiling(groupCount * lemmingDropoffTurnCapacity);
                if (neededLemmingCount > 0)
                {
                    FrontDistance = Math.Min(distance, FrontDistance);

                    int lemmingsInfected = 0;
                    for (int i = startIndex; i < index; i++)
                    {
                        if (lemmingsInfected == neededLemmingCount)
                        {
                            break;
                        }

                        currentShip = ShipsSorted[i];
                        if (IsImmune(currentShip) || currentShip.Role == ShipRole.Lemming)
                        {
                            continue;
                        }

                        SetShipRole(currentShip, ShipRole.Lemming);
                        lemmingsInfected++;
                    }
                }

                outerShipCount += shipCountAtDistance;
            }
        }

        private bool IsImmune(MyShip ship)
        {
            return (ship.Role.IsHigherPriorityThan(ShipRole.Lemming) || ship.Role == ShipRole.Interceptor);
        }

        private static int GetSafeLemmingDistance(MyShip ship)
        {
            return (ship.LemmingMapPathDistance != double.MaxValue)
                ? (int)Math.Min(ship.LemmingMapPathDistance, ship.DistanceFromDropoff + 5)
                : ship.DistanceFromDropoff;
        }

        private class ShipLemmingDistanceReverseComparer : IComparer<MyShip>
        {
            public static readonly ShipLemmingDistanceReverseComparer Default = new ShipLemmingDistanceReverseComparer();

            public int Compare(MyShip x, MyShip y)
            {
                int xDistance = GetSafeLemmingDistance(x);
                int yDistance = GetSafeLemmingDistance(y);
                return Math.Sign(yDistance - xDistance);
            }
        }
    }
}
