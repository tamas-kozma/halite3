namespace Halite3.hlt
{
    public sealed class MyShip
    {
        public string Id { get; set; }
        public Position OriginPosition { get; set; }
        public Position Position { get; set; }
        public int Halite { get; set; }
        public ShipRole Role { get; set; }
        public bool HasActionAssigned { get; set; }
        public Position Destination { get; set; }
        public int BlockedTurnCount { get; set; }
    }
}
