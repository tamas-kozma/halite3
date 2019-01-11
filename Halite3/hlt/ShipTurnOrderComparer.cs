namespace Halite3.hlt
{
    using System.Collections.Generic;

    // Ships that should come last (lowest priority) compare as greatest.
    public sealed class ShipTurnOrderComparer : IComparer<MyShip>
    {
        private readonly MapLayer<int> dummyMap;

        public ShipTurnOrderComparer(MapLayer<int> dummyMap)
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

            int xDistance = dummyMap.WraparoundDistance(x.Destination, x.OriginPosition);
            int yDistance = dummyMap.WraparoundDistance(y.Destination, y.OriginPosition);
            int aspectComparisonResult = xDistance - yDistance;
            if (aspectComparisonResult != 0)
            {
                return aspectComparisonResult;
            }

            return 0;
        }
    }
}
