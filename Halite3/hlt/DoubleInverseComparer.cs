namespace Halite3.hlt
{
    using System.Collections.Generic;

    public sealed class DoubleInverseComparer : IComparer<double>
    {
        public static readonly DoubleInverseComparer Default = new DoubleInverseComparer();

        public int Compare(double x, double y)
        {
            if (x < y)
            {
                return 1;
            }
            else if (x == y)
            {
                return 0;
            }
            else
            {
                return -1;
            }
        }
    }
}
