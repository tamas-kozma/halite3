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

            ShipsSorted.Clear();
            ShipsSorted.AddRange(MyPlayer.MyShips);
            ShipsSorted.Sort(ShipDistanceFromDropoffReverseComparer.Default);

            FrontDistance = int.MaxValue;
            double lemmingDropoffTurnCapacity = TuningSettings.LemmingDropoffTurnCapacity * MyPlayer.Dropoffs.Count;
            int index = 0;
            int outerShipCount = 0;
            while (index < ShipsSorted.Count)
            {
                var currentShip = ShipsSorted[index];
                int distance = currentShip.DistanceFromDropoff;
                int startIndex = index;
                int immuneCountAtDistance = 0;
                while (index < ShipsSorted.Count && currentShip.DistanceFromDropoff == distance)
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

        private class ShipDistanceFromDropoffReverseComparer : IComparer<Ship>
        {
            public static readonly ShipDistanceFromDropoffReverseComparer Default = new ShipDistanceFromDropoffReverseComparer();

            public int Compare(Ship x, Ship y)
            {
                return y.DistanceFromDropoff - x.DistanceFromDropoff;
            }
        }
    }
}
