namespace Halite3
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;

    public class Player
    {
        public Logger Logger { get; set; }

        public string Id { get; protected set; }
        public Position ShipyardPosition { get; protected set; }
        public int Halite { get; protected set; }

        public int TotalReturnedHalite { get; private set; }
        public int InitialHalite { get; private set; }

        /// <summary>
        /// Including the shipyard.
        /// </summary>
        public List<Position> DropoffPositions { get; protected set; } = new List<Position>();

        public List<Ship> Ships { get; protected set; } = new List<Ship>();

        /// <summary>
        /// Ships sunk in the last turn.
        /// </summary>
        public List<Ship> Shipwrecks { get; protected set; } = new List<Ship>();

        public DataMapLayer<Ship> ShipMap { get; protected set; }
        public DataMapLayer<int> DistanceFromDropoffMap { get; protected set; }

        public virtual void Initialize(PlayerInitializationMessage playerMessage, DataMapLayer<int> initialHaliteMap)
        {
            Id = playerMessage.PlayerId;

            ShipyardPosition = playerMessage.ShipyardPosition;
            DropoffPositions.Add(ShipyardPosition);

            int mapWidth = initialHaliteMap.Width;
            int mapHeight = initialHaliteMap.Height;
            ShipMap = new DataMapLayer<Ship>(mapWidth, mapHeight);

            DistanceFromDropoffMap = new DataMapLayer<int>(mapWidth, mapHeight);
            UpdateDropoffDistances();
        }

        public void Update(TurnMessage turnMessage)
        {
            var playerMessage = turnMessage.PlayerUpdates.Single(message => message.PlayerId == Id);
            int haliteBefore = Halite;
            Halite = playerMessage.Halite;
            if (turnMessage.TurnNumber == 1)
            {
                InitialHalite = Halite;
            }

            HandleDropoffMessages(playerMessage);
            HandleShipMessages(playerMessage);

            if (turnMessage.TurnNumber != 1)
            {
                // This can be negative, but that has already been accounted for when new ships and dropoffs got registered.
                int haliteDifference = Halite - haliteBefore;
                TotalReturnedHalite += haliteDifference;
            }
        }

        protected void UpdateDropoffDistances()
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

        protected virtual void HandleDropoffMessages(PlayerUpdateMessage playerMessage)
        {
            foreach (var message in playerMessage.Dropoffs)
            {
                if (!DropoffPositions.Contains(message.Position))
                {
                    TotalReturnedHalite += GameConstants.DropoffCost;
                    DropoffPositions.Add(message.Position);
                    UpdateDropoffDistances();
                }
            }
        }

        protected virtual void HandleShipMessages(PlayerUpdateMessage playerMessage)
        {
            Shipwrecks.Clear();
            var shipMessagesById = playerMessage.Ships.ToDictionary(message => message.ShipId);
            for (int i = 0; i < Ships.Count; i++)
            {
                var ship = Ships[i];
                if (!shipMessagesById.TryGetValue(ship.Id, out var shipMessage))
                {
                    shipMessagesById.Remove(ship.Id);
                    Shipwrecks.Add(ship);
                    ShipMap[ship.Position] = null;
                    Ships.RemoveAt(i);
                    HandleSunkShip(ship);
                    i--;
                    continue;
                }
                else
                {
                    shipMessagesById.Remove(ship.Id);
                }

                HandleAliveShip(ship, shipMessage);

                // TODO: This will not work for MyShips.
                ship.PreviousPosition = ship.Position;
                ship.Position = shipMessage.Position;
                ship.Halite = shipMessage.Halite;
            }

            // New ships.
            Debug.Assert(shipMessagesById.Count <= 1);
            foreach (var shipMessage in shipMessagesById.Values)
            {
                TotalReturnedHalite += GameConstants.ShipCost;
                var ship = HandleNewShip(shipMessage);
                ship.Id = shipMessage.ShipId;
                ship.Position = shipMessage.Position;
                Ships.Add(ship);
                ShipMap[shipMessage.Position] = ship;
            }
        }

        protected virtual void HandleSunkShip(Ship ship)
        {
        }

        protected virtual void HandleAliveShip(Ship ship, ShipMessage shipMessage)
        {
        }

        protected virtual Ship HandleNewShip(ShipMessage shipMessage)
        {
            return new Ship(this);
        }

        public override string ToString()
        {
            return "Player " + Id;
        }
    }
}
