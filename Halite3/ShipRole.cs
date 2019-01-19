namespace Halite3
{
    public enum ShipRole
    {
        Dropoff = 0, // Used only when predicting the role of a builder at its destination.
        SpecialAgent = 1,
        Lemming = 2,
        Builder = 3,
        Inbound = 4,
        Harvester = 5,
        Outbound = 6,
        LowestPriority = 6
    }
}
