namespace Halite3
{
    using System.Collections.Generic;
    using System.Diagnostics;

    public sealed class OpponentPlayer : Player
    {
        public List<OpponentShip> OpponentShips { get; private set; } = new List<OpponentShip>();
        public DataMapLayer<OpponentShip> OpponentShipMap { get; private set; }

        public override void Initialize(PlayerInitializationMessage playerMessage, DataMapLayer<int> initialHaliteMap)
        {
            base.Initialize(playerMessage, initialHaliteMap);

            int mapWidth = initialHaliteMap.Width;
            int mapHeight = initialHaliteMap.Height;
            OpponentShipMap = new DataMapLayer<OpponentShip>(mapWidth, mapHeight);
        }

        public OpponentShip GetFromOpponentShipMap(Position position)
        {
            var ship = OpponentShipMap[position];
            Debug.Assert(ship == null || ship.Position == position, "position=" + position + ", ship=" + ship);
            Debug.Assert(ship == ShipMap[position], "ship=" + ship + ", ShipMap[position]=" + ShipMap[position]);
            return ship;
        }

        protected override void HandleSunkShip(Ship ship)
        {
            Debug.Assert(OpponentShipMap[ship.Position] == ship);
            OpponentShipMap[ship.Position] = null;
            bool removed = OpponentShips.Remove(ship as OpponentShip);
            Debug.Assert(removed);
        }

        protected override void HandleAliveShip(Ship ship, ShipMessage shipMessage)
        {
            ship.PreviousPosition = ship.Position;
            ship.Position = shipMessage.Position;
            if (ship.PreviousPosition != ship.Position)
            {
                var shipAtOldPosition = OpponentShipMap[ship.PreviousPosition];
                if (shipAtOldPosition == ship)
                {
                    Debug.Assert(ShipMap[ship.PreviousPosition] == shipAtOldPosition);
                    ShipMap[ship.PreviousPosition] = null;
                    OpponentShipMap[ship.PreviousPosition] = null;
                }

                ShipMap[ship.Position] = ship;
                OpponentShipMap[ship.Position] = ship as OpponentShip;
            }
            else
            {
                Debug.Assert(GetFromOpponentShipMap(ship.Position) == ship);
            }

            ship.Halite = shipMessage.Halite;

            var opponentShip = ship as OpponentShip;
        }

        protected override Ship HandleNewShip(ShipMessage shipMessage)
        {
            var ship = new OpponentShip(this);
            OpponentShips.Add(ship);
            Debug.Assert(OpponentShipMap[shipMessage.Position] == null);
            OpponentShipMap[shipMessage.Position] = ship;
            return ship;
        }
    }
}
