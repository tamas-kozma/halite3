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
        private readonly List<MyShip> shipQueue;
        private readonly ListBank<MyShip> shipListBank;

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
        private OutboundMap dangerousEarlyGameOutboundMap;
        private ReturnMap dangerousDetourReturnMap;
        private AdjustedHaliteMap dangerousDetourAdjustedHaliteMap;
        private OutboundMap dangerousDetourOutboundMap;
        private ReturnMap originReturnMap;
        private AdjustedHaliteMap originAdjustedHaliteMap;
        private OutboundMap originOutboundMap;
        private OutboundMap earlyGameOriginOutboundMap;
        private ReturnMap originDetourReturnMap;
        private AdjustedHaliteMap originDetourAdjustedHaliteMap;
        private OutboundMap originDetourOutboundMap;
        private InversePriorityShipTurnOrderComparer shipTurnOrderComparer;
        private BitMapLayer forbiddenCellsMap;
        private BitMapLayer permanentForbiddenCellsMap;
        private BitMapLayer originForbiddenCellsMap;
        private MapBooster mapBooster;
        private DataMapLayer<double> originHaliteDoubleMap;
        private bool areHaliteBasedMapsDirty;
        private int blockedShipCount;
        private DataMapLayer<Ship> allOpponentShipMap;
        private DataMapLayer<List<MyShip>> turnPredictionMap;
        private int earlyGameShipCount;
        private int earlyGameShipMinReturnedHalite;
        private PushPathCalculator pushPathCalculator;
        private int totalHaliteOnMap;
        private OpponentHarvestAreaMap opponentHarvestAreaMap;

        public Sotarto(Logger logger, Random random, HaliteEngineInterface haliteEngineInterface, TuningSettings tuningSettings)
        {
            this.logger = logger;
            this.random = random;
            this.haliteEngineInterface = haliteEngineInterface;
            this.tuningSettings = tuningSettings;

            painter = new MapLayerPainter();
            painter.CellPixelSize = 8;

            shipQueue = new List<MyShip>(100);
            shipListBank = new ListBank<MyShip>();

            earlyGameShipMinReturnedHalite = -1;
        }

        public int TurnNumber
        {
            get { return turnMessage.TurnNumber; }
        }

        public bool IsMuted { get; set; }

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
                    myPlayer.MyShips.Count <= 60
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

                ship.IsBlockedHarvesterTryingHarder = false;
                ship.IsBlockedOutboundTurnedHarvester = false;
                ship.HasFoundTooLittleHaliteToHarvestThisTurn = false;

                if (myPlayer.TotalReturnedHalite == 0
                    && ship.Position == myPlayer.ShipyardPosition 
                    && ship.Role == ShipRole.Outbound
                    && !ship.IsEarlyGameShip
                    && tuningSettings.IsEarlyGameFeatureEnabled)
                {
                    earlyGameShipCount++;
                    ship.IsEarlyGameShip = true;
                }

                shipQueue.Add(ship);
            }

            ResetHaliteDependentState();
            foreach (var ship in shipQueue)
            {
                UpdateShipDestination(ship);
            }

            Debug.Assert(!areHaliteBasedMapsDirty);
            while (shipQueue.Count != 0)
            {
                bool haliteChanged = areHaliteBasedMapsDirty;
                //logger.LogDebug("haliteChanged = " + haliteChanged);

                var bestShip = shipQueue[0];
                int bestIndex = 0;
                for (int i = 1; i < shipQueue.Count; i++)
                {
                    var ship = shipQueue[i];
                    if (haliteChanged && ship.Role == ShipRole.Outbound)
                    {
                        UpdateShipDestination(ship);
                    }

                    if (shipTurnOrderComparer.Compare(ship, bestShip) < 0)
                    {
                        bestShip = ship;
                        bestIndex = i;
                    }
                }

                if (!bestShip.HasActionAssigned)
                {
                    TryAssignOrderToShip(bestShip);
                    UpdateShipDestination(bestShip);

                    if (!bestShip.HasActionAssigned)
                    {
                        logger.LogDebug("Ship " + bestShip.Id + " at " + bestShip.OriginPosition + ", with role " + bestShip.Role + ", requested retry.");
                        continue;
                    }
                }

                if (bestShip.FugitiveForTurnCount > 0)
                {
                    bestShip.FugitiveForTurnCount--;
                    if (bestShip.FugitiveForTurnCount == 0)
                    {
                        UpdateShipDestination(bestShip);
                    }
                }

                //logger.LogDebug(bestShip.ToString());

                if (bestShip.Destination.HasValue && bestShip.Position == bestShip.Destination.Value)
                {
                    if (bestShip.Role == ShipRole.Outbound)
                    {
                        // Doing this early so that someone starting to harvest nearby doesn't prompt this ship to keep going unnecessarily.
                        // No similar problem with harvesters, as those are not affected by simulated halite changes very much.
                        SetShipRole(bestShip, ShipRole.Harvester);
                    }

                    if (bestShip.Role == ShipRole.Inbound)
                    {
                        // With this the inbound handler code doesn't need a special case for a ship that got pushed away from a dropoff.
                        SetShipRole(bestShip, ShipRole.Outbound);
                    }
                }

                if (bestShip.Role == ShipRole.Harvester || bestShip.Role == ShipRole.Outbound)
                {
                    AdjustHaliteForSimulatedHarvest(bestShip);
                }

                if (bestIndex != shipQueue.Count - 1)
                {
                    shipQueue[bestIndex] = shipQueue[shipQueue.Count - 1];
                    shipQueue[shipQueue.Count - 1] = bestShip;
                }

                shipQueue.RemoveAt(shipQueue.Count - 1);
            }
        }

        private void SetShipMap(MyShip ship)
        {
            switch (ship.Role)
            {
                case ShipRole.Outbound:
                    // Prevents infinite Outbound <-> Harverster transitions due to map differences.
                    OutboundMap outboundMap;
                    if (ship.IsOutboundGettingClose)
                    {
                        if (ship.IsEarlyGameShip)
                        {
                            Debug.Assert(earlyGameOriginOutboundMap != null);
                            outboundMap = earlyGameOriginOutboundMap;
                            //logger.LogDebug(ship + " gets earlyGameOriginOutboundMap (isEarly = " + outboundMap.IsEarlyGameMap + ").");
                        }
                        else
                        {
                            outboundMap = originOutboundMap;
                            //logger.LogDebug(ship + " gets originOutboundMap (isEarly = " + outboundMap.IsEarlyGameMap + ").");
                        }
                    }
                    else
                    {
                        if (ship.IsEarlyGameShip)
                        {
                            outboundMap = GetOutboundMap(MapSetKind.EarlyGame);
                            //logger.LogDebug(ship + " gets GetEarlyGameOutboundMap() (isEarly = " + outboundMap.IsEarlyGameMap + ").");
                        }
                        else
                        {
                            outboundMap = GetOutboundMap(MapSetKind.Default);
                            //logger.LogDebug(ship + " gets GetOutboundMap() (isEarly = " + outboundMap.IsEarlyGameMap + ").");
                        }
                    }

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

            if (ship.FugitiveForTurnCount > 0)
            {
                ship.MapDirection = ship.MapDirection * -1;
            }
        }

        private void UpdateShipDestination(MyShip ship)
        {
            SetShipMap(ship);
            if (ship.Map == null)
            {
                return;
            }

            (var optimalDestination, int optimalDestinationDistance) = FollowPath(ship);
            ship.Destination = optimalDestination;
            ship.DistanceFromDestination = optimalDestinationDistance;

            if (ship.Role == ShipRole.Outbound 
                && optimalDestinationDistance < tuningSettings.OutboundShipSwitchToOriginMapDistance
                && !ship.IsOutboundGettingClose)
            {
                ship.IsOutboundGettingClose = true;
                UpdateShipDestination(ship);
            }
        }

        private void SetShipRole(MyShip ship, ShipRole role)
        {
            logger.LogDebug(ship + " changes role to " + role + ".");
            if (ship.IsEarlyGameShip && role == ShipRole.Inbound)
            {
                ship.IsEarlyGameShip = false;
                earlyGameShipCount--;
            }

            ship.Role = role;
            ship.Map = null;
            ship.Destination = null;
            ship.IsOutboundGettingClose = false;
        }

        private void TryAssignOrderToShip(MyShip ship)
        {
            logger.LogDebug("About to assing orders to ship " + ship.Id + " at " + ship.OriginPosition + ", with role " + ship.Role + " and destination " + ship.Destination + ".");

            SetShipMap(ship);
            if (ship.FugitiveForTurnCount > 0)
            {
                TryAssignOrderToFugitiveShip(ship);
                return;
            }

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

        private void TryAssignOrderToFugitiveShip(MyShip ship)
        {
            var neighbourhoodInfo = DiscoverNeighbourhood(ship, null);
            ProcessShipOrder(ship, neighbourhoodInfo.BestAllowedPosition);
        }

        private void TryAssignOrderToInboundShip(MyShip ship)
        {
            Debug.Assert(myPlayer.DistanceFromDropoffMap[ship.OriginPosition] != 0);

            var neighbourhoodInfo = DiscoverNeighbourhood(ship, null);
            if (neighbourhoodInfo.BestAllowedPosition == ship.OriginPosition)
            {
                AssignOrderToBlockedShip(ship, neighbourhoodInfo);
                return;
            }

            ProcessShipOrder(ship, neighbourhoodInfo.BestAllowedPosition);
        }

        private void TryAssignOrderToHarvester(MyShip ship)
        {
            if (ship.IsEarlyGameShip)
            {
                int haliteLostOnTheWay = (int)(originReturnMap.CellData[ship.OriginPosition].SumHalite * GameConstants.MoveCostRatio);
                int minHaliteToReturn = GetEarlyGameShipMinReturnedHalite() + haliteLostOnTheWay + 5;
                if (ship.Halite >= minHaliteToReturn)
                {
                    logger.LogDebug(ship + " turns homeward early.");
                    SetShipRole(ship, ShipRole.Inbound);
                    return;
                }
            }

            var neighbourhoodInfo = DiscoverNeighbourhood(ship, null);

            // Handles the case when there's too little halite left in the neighbourhood.
            if (!ship.HasFoundTooLittleHaliteToHarvestThisTurn)
            {
                var outboundMap = GetOutboundMap(MapSetKind.Default);
                double pathValueAtBestPosition = outboundMap.OutboundPaths[neighbourhoodInfo.BestPosition];
                if (pathValueAtBestPosition != 0)
                {
                    double bestHaliteToPathValueRatio = neighbourhoodInfo.BestValue / pathValueAtBestPosition;
                    bool isPointlessToHarvest = (bestHaliteToPathValueRatio < tuningSettings.HarvesterToOutboundConversionMaximumHaliteRatio);
                    if (isPointlessToHarvest)
                    {
                        ship.HasFoundTooLittleHaliteToHarvestThisTurn = true;
                        var newRole = (ship.Halite <= tuningSettings.HarvesterMaximumFillForTurningOutbound) ? ShipRole.Outbound : ShipRole.Inbound;
                        logger.LogDebug("Ship " + ship.Id + " at " + ship.OriginPosition + "changes role from " + ShipRole.Harvester + " to " + newRole + " because there's not enough halite here (pathValueAtBestPosition = " + pathValueAtBestPosition + ", bestValue = " + neighbourhoodInfo.BestValue + ").");
                        SetShipRole(ship, newRole);
                        return;
                    }
                }
            }

            // Handles the case when the ship is not blocked and moving is better than staying.
            int availableCapacity = GameConstants.ShipCapacity - ship.Halite;
            if (neighbourhoodInfo.BestAllowedPosition != ship.OriginPosition)
            {
                bool wantsToMove = HarvesterWantsToMoveTo(ship, neighbourhoodInfo.OriginValue, neighbourhoodInfo.BestAllowedPosition, neighbourhoodInfo.BestAllowedValue);
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
                bool wantsToMove = HarvesterWantsToMoveTo(ship, neighbourhoodInfo.OriginValue, neighbourhoodInfo.BestPosition, neighbourhoodInfo.BestValue);
                if (wantsToMove)
                {
                    if (ShouldHarvestAt(neighbourhoodInfo.BestPosition))
                    {
                        logger.LogDebug("Harvester got blocked: " + ship + ", BestAllowedPosition=" + neighbourhoodInfo.BestAllowedPosition + ", OriginValue=" + neighbourhoodInfo.OriginValue + ", BestPosition=" + neighbourhoodInfo.BestPosition + ", BestValue=" + neighbourhoodInfo.BestValue);
                        AssignOrderToBlockedShip(ship, neighbourhoodInfo);
                        return;
                    }
                }
            }

            // What's left is the case when the ship is not blocked and staying is better than moving.
            HarvestOrGoHome(ship.OriginPosition);
            return;

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

                return IsHarvesterOverflowWithinLimits(extractableIgnoringCapacity, availableCapacity);
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

        private int GetEarlyGameShipMinReturnedHalite()
        {
            if (earlyGameShipMinReturnedHalite == -1)
            {
                int targetShipCount = (int)Math.Round((myPlayer.InitialHalite / (double)GameConstants.ShipCost) * tuningSettings.EarlyGameTargetShipRatio);
                int targetShipCountCost = targetShipCount * GameConstants.ShipCost;
                int earlyGameShipCount = myPlayer.InitialHalite / GameConstants.ShipCost;
                earlyGameShipMinReturnedHalite = (int)Math.Ceiling(targetShipCountCost / (double)earlyGameShipCount);
            }

            return earlyGameShipMinReturnedHalite;
        }

        private bool HarvesterWantsToMoveTo(MyShip ship, double originHalite, Position neighbour, double neighbourHalite)
        {
            if (neighbourHalite < 1d)
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
                    bool isOverflowAcceptable = IsHarvesterOverflowWithinLimits(nextHarvestAfterTrunLimit, GameConstants.ShipCapacity - inShipAfterTurnLimit);
                    if (!isOverflowAcceptable)
                    {
                        // This means that the ship would head home after the turn limit anyway.
                        return false;
                    }
                }
            }

            double haliteRatio = originHalite / neighbourHalite;
            double threshold = (ship.IsBlockedHarvesterTryingHarder) 
                ? tuningSettings.HarvesterBlockedMoveThresholdHaliteRatio 
                : tuningSettings.HarvesterMoveThresholdHaliteRatio;

            return (haliteRatio < threshold);
        }

        private bool IsHarvesterOverflowWithinLimits(int extractable, int localAvailableCapacity)
        {
            int overflow = extractable - localAvailableCapacity;
            double overfillRatio = overflow / (double)extractable;
            return (overfillRatio <= tuningSettings.HarvesterAllowedOverfillRatio);
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
                        AssignOrderToBlockedShip(ship, neighbourhoodInfo);
                        return;
                    }
                }

                logger.LogDebug("Outbound ship " + ship.Id + " at " + ship.OriginPosition + " starts harvesting (path value = " + neighbourhoodInfo.OriginValue + ", halite = " + originAdjustedHaliteMap.Values[ship.OriginPosition] + ", isBlocked = " + isBlocked + ").");
                SetShipRole(ship, ShipRole.Harvester);
                return;
            }

            ProcessShipOrder(ship, neighbourhoodInfo.BestAllowedPosition);
        }

        private void AssignOrderToBlockedShip(MyShip ship, NeighbourhoodInfo neighbourhoodInfo)
        {
            Debug.Assert(!ship.HasActionAssigned);
            Debug.Assert(neighbourhoodInfo.BestPosition != ship.OriginPosition);

            var desiredNeighbour = neighbourhoodInfo.BestPosition;
            bool isBlockedByOpponent = (originForbiddenCellsMap[desiredNeighbour] && myPlayer.ShipMap[desiredNeighbour] == null);
            if (ship.Role == ShipRole.Outbound)
            {
                if (ship.Destination.HasValue && ship.DistanceFromDestination == 1 
                    && !ship.IsBlockedOutboundTurnedHarvester)
                {
                    ship.IsBlockedOutboundTurnedHarvester = true;
                    SetShipRole(ship, ShipRole.Harvester);
                    return;
                }
            }

            if (ship.Role == ShipRole.Harvester)
            {
                if (isBlockedByOpponent)
                {
                    if (!ship.IsBlockedHarvesterTryingHarder)
                    {
                        ship.IsBlockedHarvesterTryingHarder = true;
                        return;
                    }

                    if (ship.Halite >= tuningSettings.HarvesterMinimumFillWhenBlockedByOpponent)
                    {
                        SetShipRole(ship, ShipRole.Inbound);
                        return;
                    }

                    // No opponent ship is currently there.
                    // If it is there, but predicted to be moving away, then the spot will not be forbidden.
                    if (allOpponentShipMap[desiredNeighbour] == null)
                    {
                        // Geronimo!
                        ProcessShipOrder(ship, desiredNeighbour, false);
                        return;
                    }
                }
                else
                {
                    if (ship.Halite >= tuningSettings.HarvesterMinimumFillDefault)
                    {
                        SetShipRole(ship, ShipRole.Inbound);
                        return;
                    }
                }
            }

            ProcessShipOrder(ship, ship.OriginPosition, true);
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
                BestAllowedNeighbourValue = double.MinValue,
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

        private (Position, int) FollowPath(MyShip ship)
        {
            int maxDistance = int.MaxValue;
            if (ship.FugitiveForTurnCount > 0)
            {
                maxDistance = ship.FugitiveForTurnCount;
            }
            else if (ship.Role == ShipRole.Harvester)
            {
                maxDistance = 1;
            }

            var map = ship.Map;
            int mapDirection = ship.MapDirection;
            var position = ship.OriginPosition;
            double value = map[position] * mapDirection;
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
                    if (distance == 0)
                    {
                        SetDesiredNextPosition(ship, position);
                    }

                    break;
                }
                else
                {
                    distance++;
                    position = bestNeighbour;
                    value = bestNeighbourValue;

                    if (distance == 1)
                    {
                        Position desiredNextPosition;
                        if (ship.Role == ShipRole.Harvester)
                        {
                            if (HarvesterWantsToMoveTo(ship, originHaliteDoubleMap[ship.OriginPosition], position, originHaliteDoubleMap[position]))
                            {
                                desiredNextPosition = position;
                            }
                            else
                            {
                                desiredNextPosition = ship.OriginPosition;
                            }
                        }
                        else
                        {
                            desiredNextPosition = position;
                        }

                        SetDesiredNextPosition(ship, desiredNextPosition);
                    }
                }
            }

            return (position, distance);
        }

        private void ClearDesiredNextPosition(MyShip ship)
        {
            if (ship.DesiredNextPosition.HasValue)
            {
                var oldDesiredPosition = ship.DesiredNextPosition.Value;
                var shipList = turnPredictionMap[oldDesiredPosition];
                bool removed = shipList.Remove(ship);
                Debug.Assert(removed);

                if (shipList.Count == 0)
                {
                    turnPredictionMap[oldDesiredPosition] = null;
                    shipListBank.Return(shipList);
                }

                ship.DesiredNextPosition = null;
            }
        }

        private void SetDesiredNextPosition(MyShip ship, Position position)
        {
            if (ship.DesiredNextPosition.HasValue)
            {
                if (ship.DesiredNextPosition.Value == position)
                {
                    Debug.Assert(turnPredictionMap[position] != null && turnPredictionMap[position].Contains(ship));
                    return;
                }

                ClearDesiredNextPosition(ship);
            }

            ship.DesiredNextPosition = position;
            var shipList = turnPredictionMap[position];
            if (shipList == null)
            {
                shipList = shipListBank.Rent();
                turnPredictionMap[position] = shipList;
            }

            Debug.Assert(!shipList.Contains(ship));
            shipList.Add(ship);
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
                        if (pushedShip.DesiredNextPosition.HasValue && pushedShip.DesiredNextPosition != pushTo)
                        {
                            logger.LogDebug(pushedShip + " gets pushed to suboptimal " + pushTo + ".");
                        }

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

            if (isBlocked && ship.BlockedTurnCount >= tuningSettings.FugitiveShipConversionMinBlockedTurnCount - 1)
            {
                Debug.Assert(ship.FugitiveForTurnCount == 0);
                if (random.NextDouble() < tuningSettings.FugitiveShipConversionRatio)
                {
                    ship.FugitiveForTurnCount = random.Next(tuningSettings.FugitiveShipMinTurnCount, tuningSettings.FugitiveShipMaxTurnCount + 1);
                    logger.LogDebug(ship + " turns fugitive.");
                    isBlocked = false;
                }
            }

            if (isBlocked)
            {
                if (ship.BlockedTurnCount == 0)
                {
                    blockedShipCount++;
                }

                ship.BlockedTurnCount++;
            }
            else
            {
                if (ship.BlockedTurnCount > 0)
                {
                    blockedShipCount--;
                }

                ship.BlockedTurnCount = 0;
            }

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
                int halite = originHaliteMap[position];
                originHaliteDoubleMap[position] = halite;
                totalHaliteOnMap += halite;
            }

            mapWidth = originHaliteMap.Width;
            mapHeight = originHaliteMap.Height;
            mapBooster = new MapBooster(mapWidth, mapHeight, tuningSettings);
            forbiddenCellsMap = new BitMapLayer(mapWidth, mapHeight);
            permanentForbiddenCellsMap = new BitMapLayer(mapWidth, mapHeight);
            shipTurnOrderComparer = new InversePriorityShipTurnOrderComparer(originHaliteMap);
            allOpponentShipMap = new DataMapLayer<Ship>(mapWidth, mapHeight);
            turnPredictionMap = new DataMapLayer<List<MyShip>>(mapWidth, mapHeight);

            pushPathCalculator = new PushPathCalculator()
            {
                TuningSettings = tuningSettings,
                Logger = logger,
                MapBooster = mapBooster,
                IsForbidden = IsForbidden,
                MyPlayer = myPlayer,
                TurnPredictionMap = turnPredictionMap
            };

            opponentHarvestAreaMap = new OpponentHarvestAreaMap(mapBooster)
            {
                Logger = logger,
                TuningSettings = tuningSettings,
                AllOpponentShipMap = allOpponentShipMap
            };

            haliteEngineInterface.Ready(Name);
        }

        private bool PrepareTurn()
        {
            turnMessage = haliteEngineInterface.ReadTurnMessage(gameInitializationMessage);
            if (turnMessage == null)
            {
                return false;
            }

            UpdatePlayers();
            UpdateHaliteMap(turnMessage);
            UpdateForbiddenCellsMap();

            originReturnMap = GetReturnMap(MapSetKind.Default);
            originAdjustedHaliteMap = GetAdjustedHaliteMap(MapSetKind.Default);
            originOutboundMap = GetOutboundMap(MapSetKind.Default);
            earlyGameOriginOutboundMap = (myPlayer.Ships.Count <= 1 || earlyGameShipCount != 0) ? GetOutboundMap(MapSetKind.EarlyGame) : null;
            originDetourReturnMap = GetReturnMap(MapSetKind.Detour);
            originDetourAdjustedHaliteMap = GetAdjustedHaliteMap(MapSetKind.Detour);
            originDetourOutboundMap = GetOutboundMap(MapSetKind.Detour);

            logger.LogInfo("Turn " + TurnNumber + ": Halite on map " + totalHaliteOnMap + ", halite returned " + myPlayer.TotalReturnedHalite + ", ships sunk this turn " + myPlayer.Shipwrecks.Count + ".");

            return true;
        }

        private void UpdatePlayers()
        {
            foreach (var player in opponentPlayers)
            {
                foreach (var ship in player.Ships)
                {
                    allOpponentShipMap[ship.Position] = null;
                }
            }

            myPlayer.Update(turnMessage);
            foreach (var player in opponentPlayers)
            {
                player.Update(turnMessage);

                foreach (var ship in player.Ships)
                {
                    allOpponentShipMap[ship.Position] = ship;
                }
            }

            foreach (var shipwreck in myPlayer.Shipwrecks)
            {
                var myShipWreck = shipwreck as MyShip;
                if (myShipWreck.BlockedTurnCount > 0)
                {
                    blockedShipCount--;
                }

                ClearDesiredNextPosition(myShipWreck);

                if (myShipWreck.IsEarlyGameShip)
                {
                    earlyGameShipCount--;
                }
            }
        }

        private bool IsForbidden(MyShip ship, Position position, bool ignoreBlocker = false)
        {
            Debug.Assert(!ship.HasActionAssigned 
                && originHaliteMap.WraparoundDistance(ship.OriginPosition, position) == 1);

            if (forbiddenCellsMap[position])
            {
                bool canBeIgnored = false;
                if (opponentPlayers.Length == 1 && tuningSettings.IsTwoPlayerAggressiveModeEnabled)
                {
                    // Yes, there will be crashes, but we'll lose the same, so I'll not get behind because of that.
                    // And being bold in general sounds like a good idea.
                    if (ship.Role == ShipRole.Harvester || ship.Role == ShipRole.Outbound)
                    {
                        var shipAtPosition = myPlayer.MyShipMap[position];
                        bool isForbiddenBecauseOfOwnShip = (shipAtPosition != null && shipAtPosition.HasActionAssigned);
                        canBeIgnored = !isForbiddenBecauseOfOwnShip;
                    }
                }

                if (!canBeIgnored)
                {
                    return true;
                }
            }

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
            return pushPathCalculator.CanPush(vip, blocker);
        }

        private void AdjustHaliteForExtraction(MyShip ship)
        {
            int halite = haliteMap[ship.Position];
            int extracted = GetExtractedAmountRegardingCapacity(halite, ship);
            if (extracted != 0)
            {
                halite -= extracted;
                haliteMap[ship.Position] = halite;
                ship.Halite += extracted;
                areHaliteBasedMapsDirty = true;
            }
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
            //logger.LogDebug("Halite before at " + ship.Destination.Value + " is " + haliteMap[ship.Destination.Value] + " (original was " + originHaliteMap[ship.Destination.Value] + ")");
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

            areHaliteBasedMapsDirty = true;
            //logger.LogDebug("Halite after at " + ship.Destination.Value + " is " + haliteMap[ship.Destination.Value]);
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
                randomSeed = 0;
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

            bool isMuted = args.Any(arg => arg == "muted");
            logger.IsMuted = isMuted;

            var tuningSettings = new TuningSettings();

            var bot = new Sotarto(logger, random, engineInterface, tuningSettings);
            bot.IsMuted = isMuted;

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
            dangerousEarlyGameOutboundMap = null;
            dangerousDetourReturnMap = null;
            dangerousDetourAdjustedHaliteMap = null;
            dangerousDetourOutboundMap = null;
            areHaliteBasedMapsDirty = false;
        }

        private ReturnMap GetReturnMap(MapSetKind kind)
        {
            if (areHaliteBasedMapsDirty)
            {
                ResetHaliteDependentState();
            }

            switch (kind)
            {
                case MapSetKind.Default:
                    return GetReturnMap(ref dangerousReturnMap, permanentForbiddenCellsMap);
                case MapSetKind.Detour:
                    return GetReturnMap(ref dangerousDetourReturnMap, originForbiddenCellsMap);
                default:
                    throw new ArgumentException();
            }
        }

        private ReturnMap GetReturnMap(ref ReturnMap mapStorage, BitMapLayer forbiddenCellsMapToUse)
        {
            if (mapStorage == null)
            {
                mapStorage = new ReturnMap()
                {
                    HaliteMap = haliteMap,
                    TuningSettings = tuningSettings,
                    Logger = logger,
                    MyPlayer = myPlayer,
                    MapBooster = mapBooster,
                    ForbiddenCellsMap = forbiddenCellsMapToUse
                };

                mapStorage.Calculate();
            }

            return mapStorage;
        }

        private AdjustedHaliteMap GetAdjustedHaliteMap(MapSetKind kind)
        {
            if (areHaliteBasedMapsDirty)
            {
                ResetHaliteDependentState();
            }

            switch (kind)
            {
                case MapSetKind.Default:
                    return GetAdjustedHaliteMap(ref dangerousAdjustedHaliteMap, GetReturnMap(kind), permanentForbiddenCellsMap);
                case MapSetKind.Detour:
                    return GetAdjustedHaliteMap(ref dangerousDetourAdjustedHaliteMap, GetReturnMap(kind), originForbiddenCellsMap);
                default:
                    throw new ArgumentException();
            }
        }

        private AdjustedHaliteMap GetAdjustedHaliteMap(ref AdjustedHaliteMap mapStorage, ReturnMap returnMapToUse, BitMapLayer forbiddenCellsMapToUse)
        {
            if (mapStorage == null)
            {
                mapStorage = new AdjustedHaliteMap()
                {
                    TuningSettings = tuningSettings,
                    BaseHaliteMap = haliteMap,
                    GameInitializationMessage = gameInitializationMessage,
                    TurnMessage = turnMessage,
                    ReturnMap = returnMapToUse,
                    Logger = logger,
                    MapBooster = mapBooster,
                    ForbiddenCellsMap = forbiddenCellsMapToUse,
                    OpponentHarvestAreaMap = opponentHarvestAreaMap
                };

                mapStorage.Calculate();
            }

            return mapStorage;
        }

        private OutboundMap GetOutboundMap(MapSetKind kind)
        {
            if (areHaliteBasedMapsDirty)
            {
                ResetHaliteDependentState();
            }

            switch (kind)
            {
                case MapSetKind.Default:
                    return GetOutboundMap(ref dangerousOutboundMap, false, GetAdjustedHaliteMap(kind), permanentForbiddenCellsMap);
                case MapSetKind.Detour:
                    return GetOutboundMap(ref dangerousDetourOutboundMap, false, GetAdjustedHaliteMap(kind), originForbiddenCellsMap);
                case MapSetKind.EarlyGame:
                    return GetOutboundMap(ref dangerousEarlyGameOutboundMap, true, GetAdjustedHaliteMap(MapSetKind.Default), permanentForbiddenCellsMap);
                default:
                    throw new ArgumentException();
            }
        }

        private OutboundMap GetOutboundMap(ref OutboundMap mapStorage, bool isEarlyGameMap, AdjustedHaliteMap adjustedHaliteMapToUse, BitMapLayer forbiddenCellsMapToUse)
        {
            if (mapStorage == null)
            {
                mapStorage = new OutboundMap()
                {
                    TuningSettings = tuningSettings,
                    AdjustedHaliteMap = adjustedHaliteMapToUse,
                    MyPlayer = myPlayer,
                    Logger = logger,
                    MapBooster = mapBooster,
                    ForbiddenCellsMap = forbiddenCellsMapToUse,
                    IsEarlyGameMap = isEarlyGameMap
                };

                mapStorage.Calculate();
            }

            return mapStorage;
        }

        private void UpdateHaliteMap(TurnMessage turnMessage)
        {
            var opponentHarvestPositionList = new List<Position>();
            foreach (var cellUpdateMessage in turnMessage.MapUpdates)
            {
                var position = cellUpdateMessage.Position;
                int oldHalite = originHaliteMap[position];
                int newHalite = cellUpdateMessage.Halite;
                int haliteDifference = newHalite - oldHalite;
                totalHaliteOnMap += haliteDifference;
                originHaliteMap[position] = newHalite;
                originHaliteDoubleMap[position] = newHalite;

                if (haliteDifference < 0 && allOpponentShipMap[position] != null)
                {
                    opponentHarvestPositionList.Add(position);
                }
            }

            haliteMap = new DataMapLayer<int>(originHaliteMap);
            ResetHaliteDependentState();

            opponentHarvestAreaMap.Update(opponentHarvestPositionList);
            //PaintMap(opponentHarvestAreaMap.HaliteMultiplierMap, "HaliteMultiplierMap" + TurnNumber);
        }

        private void UpdateForbiddenCellsMap()
        {
            forbiddenCellsMap.Clear();

            var noGoDisc = new Position[permanentForbiddenCellsMap.GetDiscArea(tuningSettings.MapOpponentDropoffNoGoZoneRadius)];
            foreach (var player in opponentPlayers)
            {
                foreach (var dropoffPosition in player.DropoffPositions)
                {
                    permanentForbiddenCellsMap.GetDiscCells(dropoffPosition, tuningSettings.MapOpponentDropoffNoGoZoneRadius, noGoDisc);
                    foreach (var position in noGoDisc)
                    {
                        forbiddenCellsMap[position] = true;
                        permanentForbiddenCellsMap[position] = true;
                    }
                }

                foreach (var ship in player.Ships)
                {
                    var shipPosition = ship.Position;
                    var shipOwner = ship.Owner;
                    int shipMyDropoffdistance = myPlayer.DistanceFromDropoffMap[ship.Position];
                    if (myPlayer.DistanceFromDropoffMap[shipPosition] <= tuningSettings.MapOpponentShipInvisibilityRadius)
                    {
                        logger.LogDebug(ship + " belonging to " + shipOwner + " came too close (" + shipMyDropoffdistance + ") and thus became invisible.");
                        continue;
                    }

                    forbiddenCellsMap[ship.Position] = true;

                    var shipNeighbourArray = mapBooster.GetNeighbours(shipPosition);
                    int haliteAtShipCell = originHaliteMap[shipPosition];
                    int shipOwnerDropoffdistance = shipOwner.DistanceFromDropoffMap[ship.Position];
                    bool isHarvester = (ship.Halite >= tuningSettings.OpponentShipLikelyHarvesterMinHalite
                        && ship.Halite <= tuningSettings.OpponentShipLikelyHarvesterMaxHalite);

                    foreach (var position in shipNeighbourArray)
                    {
                        if (isHarvester)
                        {
                            int haliteAtNeighbour = originHaliteMap[position];
                            double haliteRatio = haliteAtNeighbour / (double)haliteAtShipCell;
                            if (haliteRatio <= tuningSettings.OpponentShipLikelyHarvesterMoveMaxHaliteRatio)
                            {
                                int neighbourOpponentDropoffDistance = shipOwner.DistanceFromDropoffMap[position];
                                if (neighbourOpponentDropoffDistance >= shipOwnerDropoffdistance)
                                {
                                    logger.LogDebug("Opponent ship " + ship.Id + " is assumed to be a harvester that will not move to " + position + ".");
                                    continue;
                                }
                            }
                        }

                        forbiddenCellsMap[position] = true;
                    }
                }
            }

            originForbiddenCellsMap = new BitMapLayer(forbiddenCellsMap);
        }

        [Conditional("DEBUG")]
        private void PaintMap(MapLayer<int> map, string name)
        {
            if (IsMuted)
            {
                return;
            }

            string svg = painter.MapLayerToSvg(map);
            PrintSvg(svg, name);
        }

        //[Conditional("DEBUG")]
        private void PaintMap(MapLayer<double> map, string name)
        {
            if (IsMuted)
            {
                return;
            }

            string svg = painter.MapLayerToSvg(map);
            PrintSvg(svg, name);
        }

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
                get { return (BestAllowedNeighbourValue != double.MinValue); }
            }

            public Position? NullableBestAllowedNeighbourPosition
            {
                get { return (HasAllowedNeighbour) ? BestAllowedNeighbourPosition : (Position?)null; }
            }
        }
    }
}
