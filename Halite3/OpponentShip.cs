namespace Halite3
{
    using System.Collections.Generic;

    public sealed class OpponentShip : Ship
    {
        public OpponentShip(OpponentPlayer player)
            : base(player)
        {
        }

        public ShipRole? AssumedRole;
        public Position? ExpectedNextPosition;
        public double ExpectedNextPositionCertainty;
        public List<Position> PossibleNextPositions = new List<Position>();
        public bool IsOutOfFuel;
        public bool WasOutOfFuelLastTurn;

        public void ResetIntel()
        {
            AssumedRole = null;
            ExpectedNextPosition = null;
            ExpectedNextPositionCertainty = 0;
            PossibleNextPositions.Clear();
            WasOutOfFuelLastTurn = IsOutOfFuel;
            IsOutOfFuel = false;
        }

        protected override string ToStringCore()
        {
            return base.ToStringCore() + ", O=" + Owner + ", AR=" + AssumedRole + ", ENP=" + ExpectedNextPosition + ", ENPC=" + ExpectedNextPositionCertainty + ", PNP=" + string.Join(" ", PossibleNextPositions);
        }
    }
}
