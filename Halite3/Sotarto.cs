namespace Halite3
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;

    public sealed class Sotarto
    {
        private const string Name = "Sotarto";

        private readonly Logger logger;
        private readonly Random random;
        private readonly HaliteEngineInterface haliteEngineInterface;
        private readonly TuningSettings tuningSettings;

        private readonly MapLayerPainter painter;

        private int mapWidth;
        private int mapHeight;
        private GameInitializationMessage gameInitializationMessage;
        private TurnMessage turnMessage;
        private MyPlayer myPlayer;
        private Player[] opponentPlayers;
        private DataMapLayer<int> originHaliteMap;
        private DataMapLayer<int> haliteMap;
        private ReturnMap dangerousReturnMap;
        private AdjustedHaliteMap dangerousAdjustedHaliteMap;
        private OutboundMap dangerousOutboundMap;
        private ReturnMap originReturnMap;
        private AdjustedHaliteMap originAdjustedHaliteMap;
        private OutboundMap originOutboundMap;
        private InversePriorityShipTurnOrderComparer shipTurnOrderComparer;
        private PriorityQueue<MyShip, MyShip> shipQueue;
        private BitMapLayer forbiddenCellsMap;
        private BitMapLayer originForbiddenCellsMap;
        private MapBooster mapBooster;
        private DataMapLayer<double> originHaliteDoubleMap;

        public Sotarto(Logger logger, Random random, HaliteEngineInterface haliteEngineInterface, TuningSettings tuningSettings)
        {
            this.logger = logger;
            this.random = random;
            this.haliteEngineInterface = haliteEngineInterface;
            this.tuningSettings = tuningSettings;

            painter = new MapLayerPainter();
            painter.CellPixelSize = 8;
        }

        public void Play()
        {
            Initialize();

            while (true)
            {
                if (!PrepareTurn())
                {
                    return;
                }

                var turnStartTime = DateTime.Now;
                logger.LogDebug("------------------- " + turnMessage.TurnNumber + " -------------------");

                AssignOrdersToAllShips();

                if (//turnMessage.TurnNumber <= 250 
                    myPlayer.MyShips.Count <= 20
                    && myPlayer.Halite >= GameConstants.ShipCost
                    && !forbiddenCellsMap[myPlayer.ShipyardPosition])
                {
                    myPlayer.BuildShip();
                }

                var turnTime = DateTime.Now - turnStartTime;
                logger.LogDebug("Turn " + turnMessage.TurnNumber + " took " + turnTime + " to compute.");

                var commands = new CommandList();
                commands.PopulateFromPlayer(myPlayer);
                haliteEngineInterface.EndTurn(commands);
            }
        }

        private void AssignOrdersToAllShips()
        {
            shipQueue.Clear();
            foreach (var ship in myPlayer.MyShips)
            {
                Debug.Assert(!ship.HasActionAssigned);

                int moveCost = (int)Math.Floor(originHaliteMap[ship.OriginPosition] * GameConstants.MoveCostRatio);
                if (ship.Halite < moveCost)
                {
                    logger.LogDebug("Ship " + ship.Id + " at " + ship.OriginPosition + " has not enough halite to move (" + ship.Halite + " vs " + moveCost + ").");
                    ProcessShipOrder(ship, ship.OriginPosition);
                }

                EnqueueShipForOrders(ship);
            }

            while (shipQueue.Count > 0)
            {
                var ship = shipQueue.Dequeue();
                var destinationBefore = ship.Destination;
                UpdateShipDestination(ship);

                if (!ship.HasActionAssigned)
                {
                    TryAssignOrderToShip(ship);
                    if (!ship.HasActionAssigned)
                    {
                        logger.LogDebug("Ship " + ship.Id + " at " + ship.OriginPosition + ", with role " + ship.Role + ", requested retry.");
                        EnqueueShipForOrders(ship);
                        continue;
                    }
                }

                Debug.Assert(ship.HasActionAssigned);
                var roleBefore = ship.Role;
                UpdateShipDestination(ship);
                if (ship.Destination.HasValue && ship.DistanceFromDestination == 0)
                {
                    if (ship.Role == ShipRole.Outbound)
                    {
                        // Doing this early so that someone starting to harvest nearby doesn't prompt this ship to keep going unnecessarily.
                        // No similar problem with harvesters, as those are not affected by simulated halite changes very much.
                        SetShipRole(ship, ShipRole.Harvester);
                    }

                    if (ship.Role == ShipRole.Inbound)
                    {
                        // With this the inbound handler code doesn't need a special case for a ship that got pushed away from a dropoff.
                        SetShipRole(ship, ShipRole.Outbound);
                    }
                }

                bool hasDestinationChanged = destinationBefore != ship.Destination;
                bool hasRoleChanged = roleBefore != ship.Role;
                logger.LogDebug("Done with ship " + ship.Id + " - " + ((hasDestinationChanged)
                    ? "!!! changed destination from" + destinationBefore + " to " + ship.Destination
                    : "still heading towards " + ship.Destination)
                    + ((hasRoleChanged) ? " - ! changed role from " + roleBefore + " to " + ship.Role + "." : "."));

                if (ship.Role == ShipRole.Harvester || ship.Role == ShipRole.Outbound)
                {
                    AdjustHaliteForSimulatedHarvest(ship);
                }
            }
        }

        private void EnqueueShipForOrders(MyShip ship)
        {
            Debug.Assert(ship.OriginPosition == ship.Position);

            if (!ship.Destination.HasValue)
            {
                UpdateShipDestination(ship);
            }

            shipQueue.Enqueue(ship, ship);
        }

        private void SetShipMap(MyShip ship)
        {
            switch (ship.Role)
            {
                case ShipRole.Outbound:
                    // Prevents infinite Outbound <-> Harverster transitions due to map differences.
                    var outboundMap = (ship.IsOutboundGettingClose) ? originOutboundMap : GetOutboundMap();
                    ship.Map = outboundMap.OutboundPaths;
                    ship.MapDirection = 1;
                    break;

                case ShipRole.Harvester:
                    ship.Map = originHaliteDoubleMap;
                    ship.MapDirection = 1;
                    break;

                case ShipRole.Inbound:
                    ship.Map = originReturnMap.PathCosts;
                    ship.MapDirection = -1;
                    break;

                default:
                    ship.Map = null;
                    break;
            }
        }

        private void UpdateShipDestination(MyShip ship)
        {
            if (ship.Map == null)
            {
                SetShipMap(ship);
            }

            if (ship.Map != null)
            {
                int maxDistance = int.MaxValue;
                if (ship.Role == ShipRole.Harvester)
                {
                    maxDistance = 1;
                }

                (var optimalDestination, int optimalDestinationDistance) = FollowPath(ship.Position, ship.Map, ship.MapDirection, maxDistance);
                ship.Destination = optimalDestination;
                ship.DistanceFromDestination = optimalDestinationDistance;

                if (ship.Role == ShipRole.Outbound && optimalDestinationDistance < tuningSettings.OutboundShipSwitchToOriginMapDistance)
                {
                    ship.IsOutboundGettingClose = true;
                }
            }
        }

        private void SetShipRole(MyShip ship, ShipRole role)
        {
            logger.LogDebug(ship + " changes role to " + role + ".");
            ship.Role = role;
            ship.Map = null;
            ship.Destination = null;
            ship.IsOutboundGettingClose = false;
        }

        private void TryAssignOrderToShip(MyShip ship)
        {
            logger.LogDebug("About to assing orders to ship " + ship.Id + " at " + ship.OriginPosition + ", with role " + ship.Role + " and destination " + ship.Destination + ".");

            SetShipMap(ship);
            switch (ship.Role)
            {
                case ShipRole.Harvester:
                    TryAssignOrderToHarvester(ship);
                    break;

                case ShipRole.Outbound:
                    TryAssignOrderToOutboundShip(ship);
                    break;

                case ShipRole.Inbound:
                    TryAssignOrderToInboundShip(ship);
                    break;

                default:
                    Debug.Fail("Unexpected ship role.");
                    ProcessShipOrder(ship, ship.OriginPosition);
                    break;
            }
        }

        private void TryAssignOrderToInboundShip(MyShip ship)
        {
            Debug.Assert(myPlayer.DistanceFromDropoffMap[ship.OriginPosition] != 0);

            var neighbourhoodInfo = DiscoverNeighbourhood(ship, null);
            if (neighbourhoodInfo.BestAllowedPosition == ship.OriginPosition)
            {
                AssignOrderToBlockedShip(ship, neighbourhoodInfo.BestPosition, neighbourhoodInfo);
                return;
            }

            ProcessShipOrder(ship, neighbourhoodInfo.BestAllowedPosition);
        }

        private void TryAssignOrderToHarvester(MyShip ship)
        {
            var neighbourhoodInfo = DiscoverNeighbourhood(ship, null);

            // Handles the case when there's too little halite left in the neighbourhood.
            var outboundMap = GetOutboundMap();
            double pathValueAtBestPosition = outboundMap.OutboundPaths[neighbourhoodInfo.BestPosition];
            if (pathValueAtBestPosition != 0)
            {
                double bestHaliteToPathValueRatio = neighbourhoodInfo.BestValue / pathValueAtBestPosition;
                bool isPointlessToHarvest = (bestHaliteToPathValueRatio < tuningSettings.HarvesterToOutboundConversionMaximumHaliteRatio);
                if (isPointlessToHarvest)
                {
                    var newRole = (ship.Halite <= tuningSettings.HarvesterMaximumFillForTurningOutbound) ? ShipRole.Outbound : ShipRole.Inbound;
                    logger.LogDebug("Ship " + ship.Id + " at " + ship.OriginPosition + "changes role from " + ShipRole.Harvester + " to " + newRole + " because there's not enough halite here (pathValueAtBestPosition = " + pathValueAtBestPosition + ", bestValue = " + neighbourhoodInfo.BestValue + ").");
                    SetShipRole(ship, newRole);
                    return;
                }
            }

            // Handles the case when the ship is not blocked and moving is better than staying.
            int availableCapacity = GameConstants.ShipCapacity - ship.Halite;
            if (neighbourhoodInfo.BestAllowedPosition != ship.OriginPosition)
            {
                bool wantsToMove = WantsToMoveTo(neighbourhoodInfo.OriginValue, neighbourhoodInfo.BestAllowedPosition, neighbourhoodInfo.BestAllowedValue);
                if (wantsToMove)
                {
                    HarvestOrGoHome(neighbourhoodInfo.BestAllowedPosition);
                    return;
                }
            }

            // Handles the case when the ship is blocked.
            if (neighbourhoodInfo.BestAllowedPosition == ship.OriginPosition
                && neighbourhoodInfo.OriginValue < neighbourhoodInfo.BestValue)
            {
                bool wantsToMove = WantsToMoveTo(neighbourhoodInfo.OriginValue, neighbourhoodInfo.BestPosition, neighbourhoodInfo.BestValue);
                AssignOrderToBlockedShip(ship, neighbourhoodInfo.BestPosition, neighbourhoodInfo);
                return;
            }

            // What's left is the case when the ship is not blocked and staying is better than moving.
            HarvestOrGoHome(ship.OriginPosition);
            return;

            bool WantsToMoveTo(double originAdjustedHalite, Position neighbour, double neighbourAdjustedHalite)
            {
                if (neighbourAdjustedHalite < 1d)
                {
                    return false;
                }

                int originDropoffDistance = originReturnMap.CellData[ship.OriginPosition].Distance;
                int neighbourDropoffDistance = originReturnMap.CellData[neighbour].Distance;
                int dropoffDistanceDifference = neighbourDropoffDistance - originDropoffDistance;
                if (dropoffDistanceDifference >= 0)
                {
                    // TODO: Can it be the same?!
                    int turnLimit = 2 + dropoffDistanceDifference;
                    int originActualHalite = originHaliteMap[ship.OriginPosition];
                    int remainingAfterTurnLimit = (int)(Math.Pow(1 - GameConstants.ExtractRatio, turnLimit) * originActualHalite);
                    int inShipAfterTurnLimit = ship.Halite + (originActualHalite - remainingAfterTurnLimit);
                    if (inShipAfterTurnLimit >= GameConstants.ShipCapacity)
                    {
                        // If the ship would be full staying where it is in the same amount of time as it would take to move, harvest and 
                        // potentially come back, then statying is better.
                        return false;
                    }

                    int nextHarvestAfterTrunLimit = GetExtractedAmountIgnoringCapacity(remainingAfterTurnLimit);
                    if (inShipAfterTurnLimit + nextHarvestAfterTrunLimit > GameConstants.ShipCapacity)
                    {
                        bool isOverflowAcceptable = IsOverflowWithinLimits(nextHarvestAfterTrunLimit, GameConstants.ShipCapacity - inShipAfterTurnLimit);
                        if (!isOverflowAcceptable)
                        {
                            // This means that the ship would head home after the turn limit anyway.
                            return false;
                        }
                    }
                }

                double haliteRatio = originAdjustedHalite / neighbourAdjustedHalite;
                return (haliteRatio < tuningSettings.HarvesterMoveThresholdHaliteRatio);
            }

            bool ShouldHarvestAt(Position position)
            {
                int halite = originHaliteMap[position];
                int extractableIgnoringCapacity = GetExtractedAmountIgnoringCapacity(halite);
                if (extractableIgnoringCapacity == 0)
                {
                    return false;
                }

                if (ship.Halite < tuningSettings.HarvesterMinimumFillDefault
                    || availableCapacity >= extractableIgnoringCapacity)
                {
                    return true;
                }

                return IsOverflowWithinLimits(extractableIgnoringCapacity, availableCapacity);
            }

            bool IsOverflowWithinLimits(int extractable, int localAvailableCapacity)
            {
                int overflow = extractable - localAvailableCapacity;
                double overfillRatio = overflow / (double)extractable;
                return (overfillRatio <= tuningSettings.HarvesterAllowedOverfillRatio);
            }

            void HarvestOrGoHome(Position position)
            {
                if (ShouldHarvestAt(position))
                {
                    ProcessShipOrder(ship, position);
                }
                else
                {
                    SetShipRole(ship, ShipRole.Inbound);
                }
            }
        }

        private void TryAssignOrderToOutboundShip(MyShip ship)
        {
            var neighbourhoodInfo = DiscoverNeighbourhood(ship,
                (candidate, bestSoFar) =>
                {
                    if (ship.Destination.HasValue)
                    {
                        int candidateMaxDimensionDistance = originHaliteMap.MaxSingleDimensionDistance(candidate, ship.Destination.Value);
                        int bestSoFarMaxDimensionDistance = originHaliteMap.MaxSingleDimensionDistance(bestSoFar, ship.Destination.Value);
                        int difference = Math.Abs(candidateMaxDimensionDistance - bestSoFarMaxDimensionDistance);
                        if (difference > tuningSettings.OutboundShipAntiSquarePathMinDifference)
                        {
                            return (candidateMaxDimensionDistance < bestSoFarMaxDimensionDistance);
                        }
                    }

                    return (originHaliteMap[candidate] < originHaliteMap[bestSoFar]);
                });

            if (neighbourhoodInfo.BestAllowedPosition == ship.OriginPosition)
            {
                bool isBlocked = neighbourhoodInfo.BestValue > neighbourhoodInfo.BestAllowedValue;
                if (isBlocked)
                {
                    bool hasArrived = false;
                    double originAdjustedHalite = originAdjustedHaliteMap.Values[ship.OriginPosition];
                    if (neighbourhoodInfo.OriginValue != 0)
                    {
                        double originHaliteToPathValueRatio = originAdjustedHalite / neighbourhoodInfo.OriginValue;
                        hasArrived = (originHaliteToPathValueRatio >= tuningSettings.OutboundShipToHarvesterConversionMinimumHaliteRatio);
                    }

                    if (!hasArrived)
                    {
                        AssignOrderToBlockedShip(ship, neighbourhoodInfo.BestPosition, neighbourhoodInfo);
                        return;
                    }
                }

                logger.LogDebug("Outbound ship " + ship.Id + " at " + ship.OriginPosition + " starts harvesting (path value = " + neighbourhoodInfo.OriginValue + ", halite = " + originAdjustedHaliteMap.Values[ship.OriginPosition] + ", isBlocked = " + isBlocked + ").");
                SetShipRole(ship, ShipRole.Harvester);
                return;
            }

            ProcessShipOrder(ship, neighbourhoodInfo.BestAllowedPosition);
        }

        // TODO: Implement. What I have here now is just temporary.
        private void AssignOrderToBlockedShip(MyShip ship, Position desiredNeighbour, NeighbourhoodInfo neighbourhoodInfo)
        {
            var targetPosition = ship.OriginPosition;
            if (ship.Role != ShipRole.Inbound)
            {
                int originDistanceFromDropoff = myPlayer.DistanceFromDropoffMap[ship.OriginPosition];
                bool isAroundDropoff = (originDistanceFromDropoff <= 1);
                if (isAroundDropoff)
                {
                    var bestAvailableNeighbour = neighbourhoodInfo.NullableBestAllowedNeighbourPosition;
                    if (bestAvailableNeighbour.HasValue
                        && myPlayer.DistanceFromDropoffMap[bestAvailableNeighbour.Value] > originDistanceFromDropoff)
                    {
                        targetPosition = bestAvailableNeighbour.Value;
                    }
                }
            }

            if (targetPosition == ship.OriginPosition && ship.BlockedTurnCount > 2)
            {
                if (myPlayer.MyShipMap[neighbourhoodInfo.BestPosition] == null)
                {
                    targetPosition = neighbourhoodInfo.BestPosition;
                }
            }

            ProcessShipOrder(ship, targetPosition, true);
        }

        private NeighbourhoodInfo DiscoverNeighbourhood(MyShip ship, Func<Position, Position, bool> tiebreakerIsBetter)
        {
            Debug.Assert(ship.Map != null);
            Debug.Assert(!ship.HasActionAssigned);

            // It is always allowed to stay put for now (later I might want to flee from bullies).
            double originValue = ship.MapDirection * ship.Map[ship.OriginPosition];
            var info = new NeighbourhoodInfo()
            {
                OriginValue = originValue,
                OriginPosition = ship.OriginPosition,
                BestAllowedValue = originValue,
                BestAllowedPosition = ship.OriginPosition,
                BestValue = originValue,
                BestPosition = ship.OriginPosition,
                BestAllowedNeighbourValue = -1d,
                BestAllowedNeighbourPosition = default(Position)
            };

            var neighbourArray = mapBooster.GetNeighbours(ship.OriginPosition);
            foreach (var candidatePosition in neighbourArray)
            {
                double pathValue = ship.MapDirection * ship.Map[candidatePosition];
                UpdateIfBetter(candidatePosition, pathValue, ref info.BestPosition, ref info.BestValue);
                if (!IsForbidden(ship, candidatePosition))
                {
                    UpdateIfBetter(candidatePosition, pathValue, ref info.BestAllowedPosition, ref info.BestAllowedValue);
                    UpdateIfBetter(candidatePosition, pathValue, ref info.BestAllowedNeighbourPosition, ref info.BestAllowedNeighbourValue);
                }
            }

            return info;

            void UpdateIfBetter(Position candidate, double candidateValue, ref Position bestSoFar, ref double bestSoFarValue)
            {
                if (candidateValue > bestSoFarValue 
                    || (tiebreakerIsBetter != null 
                        && candidateValue == bestSoFarValue 
                        && tiebreakerIsBetter.Invoke(candidate, bestSoFar)))
                {
                    bestSoFar = candidate;
                    bestSoFarValue = candidateValue;
                }
            }
        }

        private (Position, int) FollowPath(Position start, DataMapLayer<double> map, int mapDirection, int maxDistance = int.MaxValue)
        {
            var position = start;
            double value = map[start] * mapDirection;
            int distance = 0;
            while (distance < maxDistance)
            {
                var neighbourArray = mapBooster.GetNeighbours(position);
                double bestNeighbourValue = double.MinValue;
                var bestNeighbour = default(Position);
                foreach (var neighbour in neighbourArray)
                {
                    double neighbourValue = map[neighbour] * mapDirection;
                    if (neighbourValue > bestNeighbourValue)
                    {
                        bestNeighbour = neighbour;
                        bestNeighbourValue = neighbourValue;
                    }
                }

                if (bestNeighbourValue <= value)
                {
                    break;
                }
                else
                {
                    distance++;
                    position = bestNeighbour;
                    value = bestNeighbourValue;
                }
            }

            return (position, distance);
        }

        private void ProcessShipOrder(MyShip ship, Position position, bool isBlocked = false)
        {
            Debug.Assert(!ship.HasActionAssigned);

            logger.LogDebug("Ship " + ship.Id + " at " + ship.OriginPosition + ", with role " + ship.Role + ", got ordered to " + position + " (isBlocked = " + isBlocked + ").");

            if (position != ship.OriginPosition)
            {
                var blocker = myPlayer.MyShipMap[position];
                if (blocker != null)
                {
                    Debug.Assert(blocker.PushPath != null && blocker.PushPath.Count >= 3 && blocker.PushPath.Last() == ship.OriginPosition, "ship = " + ship + ", blocker = " + blocker + ", push path = " + string.Join(" <- ", blocker.PushPath));
                    var pushPath = blocker.PushPath;
                    bool isSwitch = (pushPath.Peek() == ship.OriginPosition);
                    Debug.Assert(!isSwitch || pushPath.Count == 3);
                    logger.LogDebug(ship + " pushes to " + position + " (" + string.Join(" <- ", pushPath) + ").");

                    Position pushFrom;
                    Position pushTo = pushPath.Pop();
                    Debug.Assert(pushTo == ship.OriginPosition || myPlayer.MyShipMap[pushTo] == null, "Push-to position has unexpected ship (" + myPlayer.MyShipMap[pushTo] + ".");
                    while (pushPath.Count >= 2)
                    {
                        pushFrom = pushPath.Pop();
                        var pushedShip = myPlayer.MyShipMap[pushFrom];
                        Debug.Assert(pushedShip != null && !pushedShip.HasActionAssigned);
                        ProcessShipOrderCore(pushedShip, pushTo, false);
                        pushTo = pushFrom;
                    }

                    Debug.Assert(pushTo == position);
                }
            }

            ProcessShipOrderCore(ship, position, isBlocked);
        }

        private void ProcessShipOrderCore(MyShip ship, Position position, bool isBlocked)
        {
            ship.Position = position;
            forbiddenCellsMap[position] = true;
            ship.HasActionAssigned = true;
            ship.BlockedTurnCount = (isBlocked) ? ship.BlockedTurnCount + 1 : 0;

            if (ship.OriginPosition != position)
            {
                var shipAtOrigin = myPlayer.MyShipMap[ship.OriginPosition];
                if (shipAtOrigin == ship)
                {
                    myPlayer.ShipMap[ship.OriginPosition] = null;
                    myPlayer.MyShipMap[ship.OriginPosition] = null;
                }
                else
                {
                    // Can only happen when switching places.
                    Debug.Assert(shipAtOrigin != null && shipAtOrigin.HasActionAssigned && shipAtOrigin.OriginPosition == position, "ship=" + ship + ", shipAtOrigin=" + shipAtOrigin);
                }

                myPlayer.ShipMap[ship.Position] = ship;
                myPlayer.MyShipMap[ship.Position] = ship;
            }
            else
            {
                AdjustHaliteForExtraction(ship);
            }
        }

        private void Initialize()
        {
            gameInitializationMessage = haliteEngineInterface.ReadGameInitializationMessage();
            GameConstants.PopulateFrom(gameInitializationMessage.GameConstants);

            string myPlayerId = gameInitializationMessage.MyPlayerId;
            myPlayer = new MyPlayer();
            opponentPlayers = new Player[gameInitializationMessage.Players.Length - 1];
            int opponentIndex = 0;
            foreach (var playerInitializationMessage in gameInitializationMessage.Players)
            {
                Player player;
                if (playerInitializationMessage.PlayerId == myPlayerId)
                {
                    player = myPlayer;
                }
                else
                {
                    player = new Player();
                    opponentPlayers[opponentIndex] = player;
                    opponentIndex++;
                }

                player.Logger = logger;
                player.Initialize(playerInitializationMessage, gameInitializationMessage.MapWithHaliteAmounts);
            }

            originHaliteMap = new DataMapLayer<int>(gameInitializationMessage.MapWithHaliteAmounts);
            originHaliteDoubleMap = new DataMapLayer<double>(originHaliteMap.Width, originHaliteMap.Height);
            foreach (var position in originHaliteMap.AllPositions)
            {
                originHaliteDoubleMap[position] = originHaliteMap[position];
            }

            mapWidth = originHaliteMap.Width;
            mapHeight = originHaliteMap.Height;
            mapBooster = new MapBooster(mapWidth, mapHeight, tuningSettings);
            forbiddenCellsMap = new BitMapLayer(mapWidth, mapHeight);
            shipTurnOrderComparer = new InversePriorityShipTurnOrderComparer(originHaliteMap);
            shipQueue = new PriorityQueue<MyShip, MyShip>(100, shipTurnOrderComparer);

            haliteEngineInterface.Ready(Name);
        }

        private bool PrepareTurn()
        {
            turnMessage = haliteEngineInterface.ReadTurnMessage(gameInitializationMessage);
            if (turnMessage == null)
            {
                return false;
            }

            myPlayer.Update(turnMessage);
            foreach (var player in opponentPlayers)
            {
                player.Update(turnMessage);
            }

            UpdateHaliteMap(turnMessage);

            forbiddenCellsMap.Clear();
            
            // TODO: Mark also dropoffs?
            // TODO: Don't mark ships close to us.
            MarkOpponentShipyardsAsForbidden();
            var opponentShips = turnMessage.PlayerUpdates
                .Where(message => message.PlayerId != myPlayer.Id)
                .SelectMany(message => message.Ships);

            var dangerousPositionArray = new Position[4];
            foreach (var shipMessage in opponentShips)
            {
                var shipPosition = shipMessage.Position;
                forbiddenCellsMap[shipPosition] = true;

                forbiddenCellsMap.GetNeighbours(shipPosition, dangerousPositionArray);
                int haliteAtShipCell = originHaliteMap[shipPosition];
                bool isHarvester = (shipMessage.Halite > 150 && shipMessage.Halite < 750);
                foreach (var position in dangerousPositionArray)
                {
                    if (isHarvester && originHaliteMap[position] < haliteAtShipCell)
                    {
                        continue;
                    }

                    forbiddenCellsMap[position] = true;
                }
            }

            originForbiddenCellsMap = new BitMapLayer(forbiddenCellsMap);
            originReturnMap = GetReturnMap();
            originAdjustedHaliteMap = GetAdjustedHaliteMap();
            originOutboundMap = GetOutboundMap();

            return true;
        }

        private bool IsForbidden(MyShip ship, Position position, bool ignoreBlocker = false)
        {
            Debug.Assert(!ship.HasActionAssigned 
                && originHaliteMap.WraparoundDistance(ship.OriginPosition, position) == 1);

            if (forbiddenCellsMap[position])
            {
                return true;
            }

            // TODO: Doing it like the below code does leads to too much complication. Change the outbound map instead to leave out
            // dropoff neighbours. Then add a calculated layer on top of the outbound paths that fills in the gaps, and use that when
            // a ship is on one of the "forbiden" cells. Maybe...
            /*if (ship.Role == ShipRole.Outbound)
            {
                // Outbound ships should not wander through dropoffs.
                int distanceFromDropoff = myPlayer.DistanceFromDropoffMap[position];
                if (distanceFromDropoff <= 1)
                {
                    int originDistanceFromDropoff = myPlayer.DistanceFromDropoffMap[ship.OriginPosition];
                    if (distanceFromDropoff < originDistanceFromDropoff)
                    {
                        return true;
                    }
                }
            }*/

            if (!ignoreBlocker)
            {
                var blocker = myPlayer.MyShipMap[position];
                if (blocker != null)
                {
                    Debug.Assert(blocker != ship && !blocker.HasActionAssigned);

                    bool canPush = GetPushPath(ship, blocker);
                    if (!canPush)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool GetPushPath(MyShip vip, MyShip blocker)
        {
            var pushPath = new Stack<Position>();
            pushPath.Push(vip.OriginPosition);
            bool canPush = GetPushPathRecursive(vip, blocker, pushPath);
            if (canPush)
            {
                logger.LogDebug(vip + " considers pushing " + blocker + " (" + string.Join(" <- ", pushPath) + ").");
            }
            else
            {
                var predictedBlockerRole = PredictRoleNextTurnBeforeForcePush(blocker);
                if (vip.Role.IsHigherPriorityThan(predictedBlockerRole))
                {
                    logger.LogDebug(vip + " considers switching places by force with " + blocker + ".");
                    pushPath.Push(blocker.OriginPosition);
                    pushPath.Push(vip.OriginPosition);
                    canPush = true;
                }
            }

            if (canPush)
            {
                blocker.PushPath = pushPath;
                return true;
            }

            return false;
        }

        private ShipRole PredictRoleNextTurnBeforeForcePush(MyShip ship)
        {
            if (ship.Role == ShipRole.Harvester)
            {
                if (ship.Halite >= tuningSettings.HarvesterMinimumFillDefault)
                {
                    return ShipRole.Inbound;
                }

                return ship.Role;
            }

            if (!ship.Destination.HasValue
                || ship.DistanceFromDestination != 0
                || ship.Role == ShipRole.SpecialAgent)
            {
                return ship.Role;
            }

            switch (ship.Role)
            {
                case ShipRole.Builder:
                    return ShipRole.Dropoff;
                case ShipRole.Inbound:
                    // No longer strictly necessary since early role changes got added to the main loop.
                    return ShipRole.Outbound;
                case ShipRole.Outbound:
                    // No longer strictly necessary since early role changes got added to the main loop.
                    return ShipRole.Harvester;
                default:
                    Debug.Fail("Shuld have been handled already: " + ship + ".");
                    return ship.Role;
            }
        }

        private bool GetPushPathRecursive(MyShip pusher, MyShip blocker, Stack<Position> pushPath)
        {
            Debug.Assert(!pusher.HasActionAssigned
                && !blocker.HasActionAssigned
                && originHaliteMap.WraparoundDistance(pusher.OriginPosition, blocker.OriginPosition) == 1);

            if (!blocker.Destination.HasValue
                || blocker.Destination == blocker.OriginPosition
                || blocker.Map == null)
            {
                return false;
            }

            var allowedNeighboursOrdered = mapBooster
                .GetNeighbours(blocker.OriginPosition)
                .Where(position => !IsForbidden(blocker, position, true))
                .OrderByDescending(position => blocker.MapDirection * blocker.Map[position]);

            pushPath.Push(blocker.OriginPosition);
            double originValue = blocker.MapDirection * blocker.Map[blocker.OriginPosition];
            foreach (var position in allowedNeighboursOrdered)
            {
                double value = blocker.MapDirection * blocker.Map[position];
                if (value < originValue)
                {
                    break;
                }

                Debug.Assert(myPlayer.MyShipMap[pusher.OriginPosition] == pusher);
                var blockerBlocker = myPlayer.MyShipMap[position];
                if (blockerBlocker != null)
                {
                    if (position == pusher.OriginPosition)
                    {
                        if (pushPath.Count == 2)
                        {
                            pushPath.Push(position);
                            return true;
                        }
                    }
                    else
                    {
                        if (pushPath.Contains(position))
                        {
                            continue;
                        }

                        bool canPush = GetPushPathRecursive(blocker, blockerBlocker, pushPath);
                        if (canPush)
                        {
                            return true;
                        }
                    }
                }
            }

            pushPath.Pop();
            return false;
        }

        private void MarkOpponentShipyardsAsForbidden()
        {
            foreach (var playerMessage in gameInitializationMessage.Players)
            {
                if (playerMessage.PlayerId == myPlayer.Id)
                {
                    continue;
                }

                forbiddenCellsMap[playerMessage.ShipyardPosition] = true;
            }
        }

        private void AdjustHaliteForExtraction(MyShip ship)
        {
            int halite = haliteMap[ship.Position];
            int extracted = GetExtractedAmountRegardingCapacity(halite, ship);
            halite -= extracted;
            haliteMap[ship.Position] = halite;
            ship.Halite += extracted;

            ResetHaliteDependentState();
        }

        private int GetExtractedAmountIgnoringCapacity(int halite)
        {
            return (int)Math.Ceiling(halite * GameConstants.ExtractRatio);
        }

        private int GetExtractedAmountRegardingCapacity(int halite, MyShip ship)
        {
            int availableCapacity = GameConstants.ShipCapacity - ship.Halite;
            return Math.Min(GetExtractedAmountIgnoringCapacity(halite), availableCapacity);
        }

        private void AdjustHaliteForSimulatedHarvest(MyShip ship)
        {
            if (!ship.Destination.HasValue)
            {
                return;
            }

            logger.LogDebug("Simulating harvest for " + ship + ".");
            var position = ship.Destination.Value;
            int localHalite = haliteMap[position];
            int haliteInShip = ship.Halite;
            while (true)
            {
                var neighbourArray = mapBooster.GetNeighbours(position);
                int maxHalite = 0;
                var bestPosition = default(Position);
                foreach (var neighbour in neighbourArray)
                {
                    int neighbourHalite = haliteMap[neighbour];
                    if (neighbourHalite > maxHalite)
                    {
                        maxHalite = neighbourHalite;
                        bestPosition = neighbour;
                    }
                }

                if (maxHalite == 0)
                {
                    // Highly unlikely, staying.
                }
                else
                {
                    double ratio = localHalite / (double)maxHalite;
                    if (ratio < tuningSettings.HarvesterMoveThresholdHaliteRatio)
                    {
                        position = bestPosition;
                        localHalite = maxHalite;
                    }
                }

                int availableCapacity = GameConstants.ShipCapacity - haliteInShip;
                int extractableAmount = GetExtractedAmountIgnoringCapacity(localHalite);
                if (extractableAmount == 0
                    || (haliteInShip > tuningSettings.HarvesterMinimumFillDefault 
                        && extractableAmount > availableCapacity))
                {
                    break;
                }

                //logger.WriteMessage("Simulated harvest at " + position + " from " + haliteMap[position] + " to " + (localHalite - extractableAmount) + ".");
                int extractedAmount = Math.Min(extractableAmount, availableCapacity);
                localHalite -= extractedAmount;
                haliteMap[position] = localHalite;
                haliteInShip += extractedAmount;
            }
        }

        public static void Main(string[] args)
        {
            string timestamp = DateTime.Now
                .ToString("s", CultureInfo.InvariantCulture)
                .Replace(':', '-');

            string randomId = Guid.NewGuid().ToString("N");
            string logPath = Name + "-" + timestamp + "-" + randomId + ".log";
            var logger = new Logger(logPath);

            int randomSeed;
            if (args.Length >= 1)
            {
                randomSeed = int.Parse(args[0]);
            }
            else
            {
                randomSeed = DateTime.Now.Millisecond;
            }

            var random = new Random(randomSeed);

            var engineInterface = new HaliteEngineInterface(logger);
            engineInterface.LogAllCommunication = true;

            string testModeArgument = args.FirstOrDefault(arg => arg.StartsWith("testModeInput"));
            if (testModeArgument != null)
            {
                engineInterface.TestMode = true;
                string testModeLinesFile = testModeArgument.Split('=')[1];
                var lines = File.ReadAllLines(testModeLinesFile);
                engineInterface.TestModeLines = lines.ToList();

                Directory.SetCurrentDirectory(Path.GetDirectoryName(testModeLinesFile));
            }

            var tuningSettings = new TuningSettings();

            var bot = new Sotarto(logger, random, engineInterface, tuningSettings);
            try
            {
                bot.Play();
            }
            catch (Exception exception)
            {
                logger.LogError(exception.ToString());
            }
        }

        private void ResetHaliteDependentState()
        {
            //logger.WriteMessage("Resetting halite dependent state.");
            dangerousReturnMap = null;
            dangerousAdjustedHaliteMap = null;
            dangerousOutboundMap = null;
        }

        private ReturnMap GetReturnMap()
        {
            if (dangerousReturnMap == null)
            {
                dangerousReturnMap = new ReturnMap()
                {
                    HaliteMap = haliteMap,
                    TuningSettings = tuningSettings,
                    Logger = logger,
                    MyPlayer = myPlayer,
                    MapBooster = mapBooster,
                    ForbiddenCellsMap = forbiddenCellsMap
                };

                dangerousReturnMap.Calculate();
            }

            return dangerousReturnMap;
        }

        private AdjustedHaliteMap GetAdjustedHaliteMap()
        {
            if (dangerousAdjustedHaliteMap == null)
            {
                dangerousAdjustedHaliteMap = new AdjustedHaliteMap()
                {
                    TuningSettings = tuningSettings,
                    BaseHaliteMap = haliteMap,
                    GameInitializationMessage = gameInitializationMessage,
                    TurnMessage = turnMessage,
                    ReturnMap = GetReturnMap(),
                    Logger = logger,
                    MapBooster = mapBooster
                };

                dangerousAdjustedHaliteMap.Calculate();
            }

            return dangerousAdjustedHaliteMap;
        }

        private OutboundMap GetOutboundMap()
        {
            if (dangerousOutboundMap == null)
            {
                dangerousOutboundMap = new OutboundMap()
                {
                    TuningSettings = tuningSettings,
                    AdjustedHaliteMap = GetAdjustedHaliteMap(),
                    MyPlayer = myPlayer,
                    Logger = logger,
                    MapBooster = mapBooster,
                    ForbiddenCellsMap = forbiddenCellsMap // TODO: Test using originForbiddenCellsMap here once blocked ships are implemented.
                };

                dangerousOutboundMap.Calculate();
            }

            return dangerousOutboundMap;
        }

        private void UpdateHaliteMap(TurnMessage turnMessage)
        {
            foreach (var cellUpdateMessage in turnMessage.MapUpdates)
            {
                originHaliteMap[cellUpdateMessage.Position] = cellUpdateMessage.Halite;
                originHaliteDoubleMap[cellUpdateMessage.Position] = cellUpdateMessage.Halite;
            }

            haliteMap = new DataMapLayer<int>(originHaliteMap);
            ResetHaliteDependentState();
        }

        [Conditional("DEBUG")]
        private void PrintMaps()
        {
            PaintMap(originHaliteMap, "haliteMap");
            PaintMap(dangerousReturnMap.PathCosts, "returnPathCosts");
            PaintMap(dangerousAdjustedHaliteMap.Values, "outboundAdjustedHaliteMap");
            PaintMap(dangerousOutboundMap.DiscAverageLayer, "outboundAdjustedAverageHaliteMap");
            PaintMap(dangerousOutboundMap.HarvestAreaMap, "outboundHarvestAreas");
            PaintMap(dangerousOutboundMap.OutboundPaths, "outboundPaths");
        }

        [Conditional("DEBUG")]
        private void PaintMap(MapLayer<int> map, string name)
        {
            string svg = painter.MapLayerToSvg(map);
            PrintSvg(svg, name);
        }

        [Conditional("DEBUG")]
        private void PaintMap(MapLayer<double> map, string name)
        {
            string svg = painter.MapLayerToSvg(map);
            PrintSvg(svg, name);
        }

        [Conditional("DEBUG")]
        private void PrintSvg(string svg, string name)
        {
            File.WriteAllText(name + "-" + myPlayer.Id + ".svg", svg);
        }

        private class NeighbourhoodInfo
        {
            public double OriginValue;
            public Position OriginPosition;
            public double BestAllowedValue;
            public Position BestAllowedPosition;
            public double BestValue;
            public Position BestPosition;
            public double BestAllowedNeighbourValue;
            public Position BestAllowedNeighbourPosition;

            public bool HasAllowedNeighbour
            {
                get { return (BestAllowedNeighbourValue != -1); }
            }

            public Position? NullableBestAllowedNeighbourPosition
            {
                get { return (HasAllowedNeighbour) ? BestAllowedNeighbourPosition : (Position?)null; }
            }
        }
    }
}
