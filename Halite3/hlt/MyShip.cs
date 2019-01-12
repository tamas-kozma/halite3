using System.Collections.Generic;

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
        public Position? Destination { get; set; }
        public int DistanceFromDestination { get; set; }
        public int BlockedTurnCount { get; set; }
        public DataMapLayer<double> Map { get; set; }
        public int MapDirection { get; set; } // 1 = climbing, -1 = descending
        public Stack<Position> PushPath { get; set; } // Only valid right after it is calculated, from the point of view of the VIP.

        public override string ToString()
        {
            return "ship-" + Id + "-[OP=" + OriginPosition + ", P=" + Position + ", H=" + Halite + ", R=" + Role + ", HasA=" + HasActionAssigned + ", D=" + Destination + ", B=" + BlockedTurnCount + "]";
        }
    }
}
