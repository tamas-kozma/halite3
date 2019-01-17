namespace Halite3
{
    public enum ShipRole
    {
        Dropoff = 0, // Used only when predicting the role of a builder at its destination.
        SpecialAgent = 1,
        Builder = 2,
        Inbound = 3,
        Harvester = 4,
        Outbound = 5,
        LowestPriority = 5
    }
}
