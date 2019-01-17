namespace Halite3
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;

    public sealed class PushPathCalculator
    {
        public TuningSettings TuningSettings;
        public Logger Logger;
        public MapBooster MapBooster;
        public Func<MyShip, Position, bool, bool> IsForbidden;
        public MyPlayer MyPlayer;
        public DataMapLayer<List<MyShip>> TurnPredictionMap;

        public bool CanPush(MyShip vip, MyShip blocker)
        {
            var pushPath = new Stack<Position>();
            pushPath.Push(vip.OriginPosition);
            bool canPush = CanPushRecursive(vip, vip, blocker, pushPath);
            if (canPush)
            {
                Logger.LogDebug(vip + " considers pushing " + blocker + " (" + string.Join(" <- ", pushPath) + ").");
            }

            if (canPush)
            {
                blocker.PushPath = pushPath;
                return true;
            }

            return false;
        }

        private bool CanPushRecursive(MyShip mainVip, MyShip vip, MyShip blocker, Stack<Position> pushPath)
        {
            Debug.Assert(!vip.HasActionAssigned
                && !blocker.HasActionAssigned
                && MapBooster.Distance(vip.OriginPosition, blocker.OriginPosition) == 1);

            var candidateList = new List<Candidate>(4);
            foreach (var neighbour in MapBooster.GetNeighbours(blocker.OriginPosition))
            {
                var desirerList = TurnPredictionMap[neighbour];
                MyShip highestPriorityDesirer = null;
                if (desirerList != null)
                {
                    foreach (var desirer in desirerList)
                    {
                        if (desirer == blocker)
                        {
                            continue;
                        }

                        if (highestPriorityDesirer == null
                            || desirer.Role.IsHigherPriorityThan(highestPriorityDesirer.Role))
                        {
                            highestPriorityDesirer = desirer;
                        }
                    }
                }

                var candidate = new Candidate()
                {
                    Calculator = this,
                    MainVip = mainVip,
                    Vip = vip,
                    Blocker = blocker,
                    Position = neighbour,
                    BlockerValue = blocker?.Map[neighbour] * blocker.MapDirection,
                    PredictedBlockerRole = PredictRoleNextTurn(blocker),
                    HighestPriorityDesirer = highestPriorityDesirer
                };

                candidateList.Add(candidate);
            }

            candidateList.Sort();

            pushPath.Push(blocker.OriginPosition);
            foreach (var candidate in candidateList)
            {
                if (!candidate.IsAllowedInPath(pushPath))
                {
                    continue;
                }

                var blockerBlocker = MyPlayer.MyShipMap[candidate.Position];
                if (blockerBlocker == null)
                {
                    pushPath.Push(candidate.Position);
                    return true;
                }

                bool canPush = CanPushRecursive(mainVip, blocker, blockerBlocker, pushPath);
                if (canPush)
                {
                    return true;
                }
            }

            pushPath.Pop();
            return false;
        }

        private class Candidate : IComparable<Candidate>
        {
            public PushPathCalculator Calculator;
            public MyShip MainVip;
            public MyShip Vip;
            public MyShip Blocker;
            public Position Position;
            public double? BlockerValue;
            public ShipRole PredictedBlockerRole;
            public MyShip HighestPriorityDesirer;

            public bool IsDesiredByBlocker
            {
                get
                {
                    if (PredictedBlockerRole != Blocker.Role
                        || !Blocker.DesiredNextPosition.HasValue)
                    {
                        return false;
                    }

                    return (Position == Blocker.DesiredNextPosition.Value);
                }
            }

            public bool IsAllowedInPath(Stack<Position> pushPath)
            {
                if (Calculator.IsForbidden(Blocker, Position, true))
                {
                    return false;
                }

                Debug.Assert(Vip != MainVip || pushPath.Count == 2);
                if (Vip != MainVip && pushPath.Contains(Position))
                {
                    return false;
                }

                if (PredictedBlockerRole.IsHigherOrEqualPriorityThan(MainVip.Role))
                {
                    // Since we don't know yet what it will want to do.
                    return false;
                }

                if (Blocker.Role.IsHigherOrEqualPriorityThan(MainVip.Role))
                {
                    return IsDesiredByBlocker;
                }

                if (GetDesirerRole().IsHigherPriorityThan(MainVip.Role))
                {
                    return false;
                }

                return true;
            }

            // Greater means less favourable.
            public int CompareTo(Candidate other)
            {
                // If the blocker is not less important than the main vip, then it can only go where it wants to go.
                if (Blocker.Role.IsHigherOrEqualPriorityThan(MainVip.Role))
                {
                    if (IsDesiredByBlocker != other.IsDesiredByBlocker)
                    {
                        return (IsDesiredByBlocker) ? -1 : 1;
                    }

                    Debug.Assert(!IsDesiredByBlocker && !other.IsDesiredByBlocker);
                    return 0;
                }

                // Outbound ships pushed by higher priority ships don't yet have up to date maps.
                // This happens a lot on dropoffs, and it results in snakes of outbound ships going in the same direction.
                // As a simple workaround for the problem, the map values will be ignored in this case, and ships will be
                // distributed more evenly. Testing shows that even ignoring desirers is a good thing.
                if (Blocker.Role == ShipRole.Outbound
                    && Calculator.MyPlayer.DistanceFromDropoffMap[Blocker.OriginPosition] == 0)
                {
                    var blockerBlocker = Calculator.MyPlayer.MyShipMap[Position];
                    var otherBlockerBLocker = Calculator.MyPlayer.MyShipMap[other.Position];
                    if (blockerBlocker == null && otherBlockerBLocker != null)
                    {
                        return -1;
                    }
                    else if (blockerBlocker != null && otherBlockerBLocker == null)
                    {
                        return 1;
                    }
                }

                var desirerRole = GetDesirerRole();
                var otherDesirerRole = other.GetDesirerRole();
                if (desirerRole != otherDesirerRole)
                {
                    return (desirerRole.IsHigherPriorityThan(otherDesirerRole)) ? 1 : -1;
                }

                if (BlockerValue.HasValue)
                {
                    Debug.Assert(other.BlockerValue.HasValue);
                    return -1 * BlockerValue.Value.CompareTo(other.BlockerValue.Value);
                }

                return 0;
            }

            private ShipRole GetDesirerRole()
            {
                if (HighestPriorityDesirer == null)
                {
                    return ShipRole.LowestPriority;
                }

                if (Blocker.Role.IsHigherPriorityThan(HighestPriorityDesirer.Role))
                {
                    return ShipRole.LowestPriority;
                }

                return HighestPriorityDesirer.Role;
            }
        }

        private ShipRole PredictRoleNextTurn(MyShip ship)
        {
            if (ship.Role == ShipRole.Harvester)
            {
                if (ship.Halite >= TuningSettings.HarvesterMinimumFillDefault)
                {
                    return ShipRole.Inbound;
                }

                return ship.Role;
            }

            if (!ship.Destination.HasValue
                || ship.DistanceFromDestination != 0
                || ship.Role == ShipRole.SpecialAgent)
            {
                return ship.Role;
            }

            switch (ship.Role)
            {
                case ShipRole.Builder:
                    return ShipRole.Dropoff;
                case ShipRole.Inbound:
                    // No longer strictly necessary since early role changes got added to the main loop.
                    return ShipRole.Outbound;
                case ShipRole.Outbound:
                    // No longer strictly necessary since early role changes got added to the main loop.
                    return ShipRole.Harvester;
                default:
                    Debug.Fail("Shuld have been handled already: " + ship + ".");
                    return ship.Role;
            }
        }
    }
}
