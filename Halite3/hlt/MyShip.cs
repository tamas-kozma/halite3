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
        public bool IsHoldingTheDoor { get; set; }

        public override string ToString()
        {
            return "ship-" + Id + "-[OP=" + OriginPosition + ", P=" + Position + ", H=" + Halite + ", R=" + Role + ", HasA=" + HasActionAssigned + ", D=" + Destination + ", B=" + BlockedTurnCount + ", IsHD=" + IsHoldingTheDoor + "]";
        }
    }
}
