namespace Halite3
{
    public sealed class PlayerInitializationMessage
    {
        public string PlayerId { get; set; }
        public Position ShipyardPosition { get; set; }
    }
}
