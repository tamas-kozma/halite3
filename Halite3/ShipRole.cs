namespace Halite3
{
    public enum ShipRole
    {
        Dropoff = 0, // Used only when predicting the role of a builder at its destination.
        SpecialAgent = 1,
        Lemming = 2,
        Interceptor = 3,
        Builder = 4,
        Inbound = 5,
        Harvester = 6,
        Outbound = 7,
        SittingDuck = 8,
        LowestPriority = 8
    }
}
