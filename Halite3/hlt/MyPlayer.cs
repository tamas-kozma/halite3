namespace Halite3.hlt
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    public sealed class MyPlayer
    {
        public string Id { get; set; }
        public Position ShipyardPosition { get; set; }
        public int Halite { get; set; }

        /// <summary>
        /// Including the shipyard.
        /// </summary>
        public List<Position> DropoffPositions { get; private set; } = new List<Position>();

        public List<MyShip> Ships { get; private set; } = new List<MyShip>();

        /// <summary>
        /// Ships sunk in the last turn.
        /// </summary>
        public List<MyShip> Shipwrecks { get; private set; } = new List<MyShip>();

        public MyShip NewShip { get; set; }

        public void BuildShip()
        {
            Debug.Assert(Halite >= GameConstants.ShipCost && NewShip == null);

            NewShip = new MyShip()
            {
                Halite = 0,
                Id = null,
                Position = ShipyardPosition
            };
        }

        public void Initialize(GameInitializationMessage initializationMessage)
        {
            Id = initializationMessage.MyPlayerId;

            var myPlayerMessage = initializationMessage.Players.Single(message => message.PlayerId == Id);
            ShipyardPosition = myPlayerMessage.ShipyardPosition;
            Halite = 5000;
            DropoffPositions.Add(ShipyardPosition);
        }

        public void Update(TurnMessage turnMessage)
        {
            var playerMessage = turnMessage.PlayerUpdates.Single(message => message.PlayerId == Id);
            Halite = playerMessage.Halite;

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
                }
            }

            NewShip = null;

            Shipwrecks.Clear();
            var shipMessagesById = playerMessage.Ships.ToDictionary(message => message.ShipId);
            for (int i = 0; i < Ships.Count; i++)
            {
                var ship = Ships[i];
                if (!shipMessagesById.TryGetValue(ship.Id, out var shipMessage))
                {
                    Shipwrecks.Add(ship);
                    Ships.RemoveAt(i);
                    i--;
                    continue;
                }

                if (ship.Position != shipMessage.Position)
                {
                    throw new BotFailedException();
                }

                ship.OriginPosition = ship.Position;
                ship.Halite = shipMessage.Halite;
                ship.HasActionAssigned = false;
            }

            if (Ships.Count != playerMessage.Ships.Length)
            {
                throw new BotFailedException();
            }
        }
    }
}
