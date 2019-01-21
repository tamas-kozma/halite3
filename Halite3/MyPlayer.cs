namespace Halite3
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    public sealed class MyPlayer : Player
    {
        public List<MyShip> MyShips { get; private set; } = new List<MyShip>();
        public DataMapLayer<MyShip> MyShipMap { get; private set; }
        public MyShip NewShip { get; private set; }
        public List<Dropoff> NewDropoffs { get; private set; } = new List<Dropoff>();
        public List<MyShip> SinkingShips { get; private set; } = new List<MyShip>();

        public override void Initialize(PlayerInitializationMessage playerMessage, DataMapLayer<int> initialHaliteMap)
        {
            base.Initialize(playerMessage, initialHaliteMap);

            int mapWidth = initialHaliteMap.Width;
            int mapHeight = initialHaliteMap.Height;
            MyShipMap = new DataMapLayer<MyShip>(mapWidth, mapHeight);
        }

        public void BuildShip()
        {
            Debug.Assert(Halite >= GameConstants.ShipCost && NewShip == null);

            Halite -= GameConstants.ShipCost;
            NewShip = new MyShip(this)
            {
                Halite = 0,
                Id = null,
                Position = ShipyardPosition,
                Role = ShipRole.Outbound,
                PreviousPosition = ShipyardPosition,
                DistanceFromDropoff = 0,
                CurrentJobStartTurn = TurnNumber
            };
        }

        public void BuildDropoff(MyShip builder)
        {
            Halite -= GameConstants.DropoffCost;
            var dropoff = new Dropoff(this)
            {
                Id = null,
                Age = 0,
                IsShipyard = false,
                Position = builder.Position
            };

            Dropoffs.Add(dropoff);
            NewDropoffs.Add(dropoff);

            UpdateDropoffDistances();

            builder.IsBuildingDropoff = true;
        }

        public MyShip GetFromMyShipMap(Position position)
        {
            var ship = MyShipMap[position];
            Debug.Assert(ship == null || (ship.Position == position && (ship.HasActionAssigned || ship.OriginPosition == position)), "position=" + position + ", ship=" + ship);
            Debug.Assert(ship == ShipMap[position], "ship=" + ship + ", myPlayer.ShipMap[position]=" + ShipMap[position]);
            return ship;
        }

        public override string ToString()
        {
            return "me";
        }

        protected override void HandleDropoffMessages(PlayerUpdateMessage playerMessage)
        {
            foreach (var dropoff in NewDropoffs)
            {
                var message = playerMessage.Dropoffs.FirstOrDefault(candidate => candidate.Position == dropoff.Position);
                if (message == null)
                {
                    Logger.LogInfo("MyPlayer: Message for new " + dropoff + " not found, assuming the builder sunk.");
                    // The builder sunk, assuming that money is not lost in this case.
                    Halite += GameConstants.DropoffCost;
                    IncomeLastTurn -= GameConstants.DropoffCost;
                    Dropoffs.Remove(dropoff);
                }
                else
                {
                    dropoff.Id = message.DropoffId;
                    Logger.LogDebug("MyPlayer: Message for new " + dropoff + " found, setting ID " + message.DropoffId + ".");
                }
            }

            NewDropoffs.Clear();

            int dropoffCountBefore = Dropoffs.Count;
            base.HandleDropoffMessages(playerMessage);
            Debug.Assert(dropoffCountBefore == Dropoffs.Count);
        }

        protected override void HandleShipMessages(PlayerUpdateMessage playerMessage)
        {
            SinkingShips.Clear();

            if (NewShip != null)
            {
                var newShipMessage = playerMessage.Ships.FirstOrDefault(shipMessage => shipMessage.Position == NewShip.Position);
                if (newShipMessage == null)
                {
                    Shipwrecks.Add(NewShip);
                }
                else
                {
                    NewShip.Id = newShipMessage.ShipId;
                    Ships.Add(NewShip);
                    MyShips.Add(NewShip);
                    Debug.Assert(ShipMap[NewShip.Position] == null && MyShipMap[NewShip.Position] == null);
                    ShipMap[NewShip.Position] = NewShip;
                    MyShipMap[NewShip.Position] = NewShip;
                }
            }

            NewShip = null;

            base.HandleShipMessages(playerMessage);
        }

        protected override void HandleSunkShip(Ship ship)
        {
            Debug.Assert(MyShipMap[ship.Position] == ship);
            MyShipMap[ship.Position] = null;
            bool removed = MyShips.Remove(ship as MyShip);
            Debug.Assert(removed);
        }

        protected override void HandleAliveShip(Ship ship, ShipMessage shipMessage)
        {
            ship.Halite = shipMessage.Halite;

            var myShip = ship as MyShip;
            if (ship.Position != shipMessage.Position)
            {
                throw new BotFailedException();
            }
            if (myShip.IsBuildingDropoff)
            {
                throw new BotFailedException();
            }

            myShip.OriginPosition = shipMessage.Position;
            myShip.HasActionAssigned = false;
        }

        protected override Ship HandleNewShip(ShipMessage shipMessage)
        {
            throw new BotFailedException();
        }
    }
}
