namespace Halite3.hlt
{
    using System.Collections.Generic;

    public sealed class ShipTurnOrderComparer : IComparer<MyShip>
    {
        private readonly MapLayer<int> dummyMap;

        public ShipTurnOrderComparer(MapLayer<int> dummyMap)
        {
            this.dummyMap = dummyMap;
        }

        public int Compare(MyShip x, MyShip y)
        {
            int aspectComparisonResult = (int)x.Role - (int)y.Role;
            if (aspectComparisonResult != 0)
            {
                return aspectComparisonResult;
            }

            int xDistance = dummyMap.WraparoundDistance(x.Destination, x.OriginPosition);
            int yDistance = dummyMap.WraparoundDistance(y.Destination, y.OriginPosition);
            aspectComparisonResult = xDistance - yDistance;
            if (aspectComparisonResult != 0)
            {
                return aspectComparisonResult;
            }

            return 0;
        }
    }
}
