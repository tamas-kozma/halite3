namespace Halite3
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;

    // Ships that should come last (lowest priority) compare as greatest.
    public sealed class InversePriorityShipTurnOrderComparer : IComparer<MyShip>
    {
        private readonly MapLayer<int> dummyMap;

        public InversePriorityShipTurnOrderComparer(MapLayer<int> dummyMap)
        {
            this.dummyMap = dummyMap;
        }

        public OutboundMap OutboundMap;

        public int Compare(MyShip x, MyShip y)
        {
            if (x.Role.IsHigherPriorityThan(y.Role))
            {
                return -1;
            }
            if (y.Role.IsHigherPriorityThan(x.Role))
            {
                return 1;
            }

            int aspectComparisonResult;
            Debug.Assert(x.Role == y.Role);
            if (x.Role == ShipRole.Harvester)
            {
                double xJobTime = OutboundMap.GetEstimatedJobTimeInNeighbourhood(x.OriginPosition, x.Halite, x.IsEarlyGameShip);
                double yJobTime = OutboundMap.GetEstimatedJobTimeInNeighbourhood(y.OriginPosition, y.Halite, y.IsEarlyGameShip);
                if (xJobTime > 0 && yJobTime > 0 && xJobTime != yJobTime)
                {
                    return Math.Sign(xJobTime - yJobTime);
                }
            }
            else
            {
                if (x.Destination.HasValue || y.Destination.HasValue)
                {
                    if (!x.Destination.HasValue)
                    {
                        return 1;
                    }
                    else if (!y.Destination.HasValue)
                    {
                        return -1;
                    }
                    else
                    {
                        aspectComparisonResult = x.DistanceFromDestination - y.DistanceFromDestination;
                        if (aspectComparisonResult != 0)
                        {
                            return aspectComparisonResult;
                        }
                    }
                }
            }

            return x.Id.CompareTo(y.Id);

            /* aspectComparisonResult = y.Halite - x.Halite;
            if (aspectComparisonResult != 0)
            {
                return aspectComparisonResult;
            }

            return 0; */
        }
    }
}
