namespace Halite3
{
    using System.Collections.Generic;
    using System.Diagnostics;

    public sealed class MyShip : Ship
    {
        public MyShip(MyPlayer myPlayer)
            : base(myPlayer)
        {
        }

        public Position OriginPosition { get; set; }
        public ShipRole Role { get; set; }
        public bool HasActionAssigned { get; set; }
        public Position? Destination { get; set; }
        public int DistanceFromDestination { get; set; }
        public int BlockedTurnCount { get; set; }
        public DataMapLayer<double> Map { get; set; }
        public int MapDirection { get; set; } // 1 = climbing, -1 = descending
        public Stack<Position> PushPath { get; set; } // Only valid right after it is calculated, from the point of view of the VIP.
        public bool IsOutboundGettingClose { get; set; }
        public Position? DesiredNextPosition { get; set; }
        public int FugitiveForTurnCount { get; set; }
        public bool IsBlockedOutboundTurnedHarvester { get; set; }
        public bool IsBlockedHarvesterTryingHarder { get; set; }
        public bool IsEarlyGameShip { get; set; }
        public bool HasFoundTooLittleHaliteToHarvestThisTurn { get; set; }
        public int DetourTurnCount { get; set; }

        public void SetPosition(Position newPosition)
        {
            Debug.Assert(Position == OriginPosition);
            PreviousPosition = Position;
            Position = newPosition;
        }

        protected override string ToStringCore()
        {
            return "OP=" + OriginPosition + ", P=" + Position + ", PP=" + PreviousPosition + ", H=" + Halite + ", R=" + Role + ", HasA=" + HasActionAssigned + ", D=" + Destination + ", DD=" + DistanceFromDestination + ", DNP=" + DesiredNextPosition + ", B=" + BlockedTurnCount + ", FT=" + FugitiveForTurnCount + ", DTC=" + DetourTurnCount;
        }
    }
}
