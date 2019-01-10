namespace Halite3.hlt
{
    public static class ShipRoleExtensions
    {
        public static bool IsHigherPriorityThan(this ShipRole self, ShipRole other)
        {
            return (int)self < (int)other;
        }

        public static bool IsHigherOrEqualPriorityThan(this ShipRole self, ShipRole other)
        {
            return (int)self <= (int)other;
        }
    }
}
