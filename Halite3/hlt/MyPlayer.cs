namespace Halite3.hlt
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

            NewShip = new MyShip()
            {
                Halite = 0,
                Id = null,
                Position = ShipyardPosition,
                Role = ShipRole.Outbound
            };
        }

        public void BuildDropoff(MyShip builder)
        {
            UpdateDropoffDistances();
            throw new NotImplementedException();
        }

        protected override void HandleDropoffMessages(PlayerUpdateMessage playerMessage)
        {
            if (playerMessage.Dropoffs.Length + 1 != DropoffPositions.Count)
            {
                throw new BotFailedException();
            }

            foreach (var position in DropoffPositions)
            {
                if (position == ShipyardPosition)
                {
                    continue;
                }

                var dropoffMessage = playerMessage.Dropoffs.FirstOrDefault(message => message.Position == position);
                if (dropoffMessage == null)
                {
                    throw new BotFailedException();
                }
            }
        }

        protected override void HandleShipMessages(PlayerUpdateMessage playerMessage)
        {
            //Logger.LogDebug("HandleShipMessages: NewShip = " + NewShip);
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
                    ShipMap[NewShip.Position] = NewShip;
                    MyShipMap[NewShip.Position] = NewShip;
                }
            }

            NewShip = null;

            base.HandleShipMessages(playerMessage);
        }

        protected override void HandleSunkShip(Ship ship)
        {
            MyShipMap[ship.Position] = null;
            MyShips.Remove(ship as MyShip);
        }

        protected override void HandleAliveShip(Ship ship, ShipMessage shipMessage)
        {
            var myShip = ship as MyShip;
            if (ship.Position != shipMessage.Position)
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
