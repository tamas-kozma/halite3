namespace Halite3.hlt
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    public sealed class MyPlayer
    {
        public string Id { get; private set; }
        public Position ShipyardPosition { get; private set; }
        public int Halite { get; private set; }

        /// <summary>
        /// Including the shipyard.
        /// </summary>
        public List<Position> DropoffPositions { get; private set; } = new List<Position>();

        public List<MyShip> Ships { get; private set; } = new List<MyShip>();

        /// <summary>
        /// Ships sunk in the last turn.
        /// </summary>
        public List<MyShip> Shipwrecks { get; private set; } = new List<MyShip>();

        public MyShip NewShip { get; private set; }
        public DataMapLayer<MyShip> ShipMap { get; private set; }

        public DataMapLayer<int> DistanceFromDropoffMap { get; private set; }

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

        public void Initialize(GameInitializationMessage initializationMessage)
        {
            Id = initializationMessage.MyPlayerId;

            var myPlayerMessage = initializationMessage.Players.Single(message => message.PlayerId == Id);
            ShipyardPosition = myPlayerMessage.ShipyardPosition;
            Halite = 5000;
            DropoffPositions.Add(ShipyardPosition);

            int mapWidth = initializationMessage.MapWithHaliteAmounts.Width;
            int mapHeight = initializationMessage.MapWithHaliteAmounts.Height;
            ShipMap = new DataMapLayer<MyShip>(mapWidth, mapHeight);

            DistanceFromDropoffMap = new DataMapLayer<int>(mapWidth, mapHeight);
            UpdateDropoffDistances();
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
                    ShipMap[NewShip.Position] = NewShip;
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
                    ShipMap[ship.Position] = null;
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

        private void UpdateDropoffDistances()
        {
            foreach (var position in DistanceFromDropoffMap.AllPositions)
            {
                int minDistance = int.MaxValue;
                foreach (var dropoffPosition in DropoffPositions)
                {
                    int distance = DistanceFromDropoffMap.WraparoundDistance(position, dropoffPosition);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                    }
                }

                DistanceFromDropoffMap[position] = minDistance;
            }
        }
    }
}
