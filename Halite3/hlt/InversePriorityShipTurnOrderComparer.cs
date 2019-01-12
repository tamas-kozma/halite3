namespace Halite3.hlt
{
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

            aspectComparisonResult = y.Halite - x.Halite;
            if (aspectComparisonResult != 0)
            {
                return aspectComparisonResult;
            }

            return 0;
        }
    }
}
