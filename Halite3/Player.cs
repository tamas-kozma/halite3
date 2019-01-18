﻿namespace Halite3
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    public abstract class Player
    {
        public Logger Logger { get; set; }

        public string Id { get; protected set; }
        public Position ShipyardPosition { get; protected set; }
        public int Halite { get; protected set; }

        public int TotalReturnedHalite { get; protected set; }
        public int InitialHalite { get; private set; }

        /// <summary>
        /// Including the shipyard.
        /// </summary>
        public List<Dropoff> Dropoffs { get; protected set; } = new List<Dropoff>();

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
            var shipyardDropoff = new Dropoff(this)
            {
                Age = 0,
                Position = ShipyardPosition,
                Id = "shipyard",
                IsShipyard = true
            };

            Dropoffs.Add(shipyardDropoff);

            int mapWidth = initialHaliteMap.Width;
            int mapHeight = initialHaliteMap.Height;
            ShipMap = new DataMapLayer<Ship>(mapWidth, mapHeight);

            DistanceFromDropoffMap = new DataMapLayer<int>(mapWidth, mapHeight);
            UpdateDropoffDistances(DistanceFromDropoffMap);
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

        public void UpdateDropoffDistances(DataMapLayer<int> map, int minAge = 0)
        {
            var eligibleDropoffPositions = Dropoffs
                .Where(dropoff => dropoff.Age >= minAge)
                .Select(dropoff => dropoff.Position)
                .ToArray();

            foreach (var position in map.AllPositions)
            {
                int minDistance = int.MaxValue;
                foreach (var dropoffPosition in eligibleDropoffPositions)
                {
                    int distance = DistanceFromDropoffMap.WraparoundDistance(position, dropoffPosition);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                    }
                }

                map[position] = minDistance;
            }
        }

        protected virtual void HandleDropoffMessages(PlayerUpdateMessage playerMessage)
        {
            var messagesById = playerMessage.Dropoffs.ToDictionary(message => message.DropoffId);
            foreach (var dropoff in Dropoffs)
            {
                if (!dropoff.IsPlanned)
                {
                    dropoff.Age++;
                }

                if (dropoff.IsShipyard || dropoff.IsPlanned)
                {
                    continue;
                }

                if (!messagesById.Remove(dropoff.Id, out var message))
                {
                    throw new BotFailedException();
                }
            }

            foreach (var message in messagesById.Values)
            {
                var dropoff = Dropoffs.FirstOrDefault(candidate => candidate.Id == message.DropoffId);
                if (dropoff == null)
                {
                    TotalReturnedHalite += GameConstants.DropoffCost;
                    dropoff = new Dropoff(this)
                    {
                        Id = message.DropoffId,
                        Age = 1,
                        IsShipyard = false,
                        Position = message.Position
                    };

                    Dropoffs.Add(dropoff);
                }
                else
                {
                    Debug.Assert(dropoff.IsPlanned && dropoff.Position == message.Position);
                    dropoff.Age = 1;
                }

                UpdateDropoffDistances(DistanceFromDropoffMap);
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
                Debug.Assert(ShipMap[shipMessage.Position] == null);
                ShipMap[shipMessage.Position] = ship;
            }
        }

        protected abstract void HandleSunkShip(Ship ship);

        protected abstract void HandleAliveShip(Ship ship, ShipMessage shipMessage);

        protected abstract Ship HandleNewShip(ShipMessage shipMessage);

        public override string ToString()
        {
            return "Player " + Id;
        }
    }
}
