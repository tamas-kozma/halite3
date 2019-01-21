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
        private OpponentPlayer[] opponentPlayers;
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
        private bool areHaliteBasedMapsDirty;
        private int blockedShipCount;
        private DataMapLayer<OpponentShip> allOpponentShipMap;
        private DataMapLayer<List<MyShip>> turnPredictionMap;
        private int earlyGameShipCount;
        private PushPathCalculator pushPathCalculator;
        private int totalHaliteOnMap;
        private OpponentHarvestAreaMap opponentHarvestAreaMap;
        private int harvesterJobsAssignedCount;
        private double meanHarvesterJobTime;
        private int totalTurnCount;
        private ExpansionMap expansionMap;
        private List<MyShip> builderList;
        private GameSimulator simulator;
        private MacroEngine macroEngine;
        private DataMapLayer<int> allOpponentDropoffDistanceMap;

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
            builderList = new List<MyShip>();
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
                DoMacroTasks();

                var turnTime = DateTime.Now - turnStartTime;
                logger.LogInfo("Turn " + turnMessage.TurnNumber + " took " + turnTime + " to compute.");

                var commands = new CommandList();
                commands.PopulateFromPlayer(myPlayer);
                haliteEngineInterface.EndTurn(commands);
            }
        }

        private void DoMacroTasks()
        {
            expansionMap.FindBestCandidates(originForbiddenCellsMap);

            macroEngine.PaintMap = PaintMap;
            var decision = macroEngine.MakeDecision(TurnNumber);

            if (!decision.BuildDropoff && builderList.Count > 0)
            {
                foreach (var builder in builderList.ToArray())
                {
                    SetShipRole(builder, ShipRole.Outbound);
                }
            }

            if (decision.BuildShip)
            {
                if (myPlayer.Halite >= GameConstants.ShipCost
                    && !forbiddenCellsMap[myPlayer.ShipyardPosition])
                {
                    myPlayer.BuildShip();
                }
            }

            if (decision.BuildDropoff)
            {
                expansionMap.CalculatePaths(decision.DropoffAreaInfo);

                if (builderList.Count == 0)
                {
                    var builderCandidate = FindBestBuilder();
                    if (builderCandidate != null)
                    {
                        SetShipRole(builderCandidate, ShipRole.Builder);
                    }
                }
            }
        }

        private void AssignOrdersToAllShips()
        {
            TurnShipsIntoLemmings();

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

                if (ship.DetourTurnCount > 0)
                {
                    ship.DetourTurnCount--;
                }

                harvesterJobsAssignedCount = 0;
                meanHarvesterJobTime = 0;

                shipQueue.Add(ship);
            }

            ResetHaliteDependentState();
            foreach (var ship in shipQueue)
            {
                UpdateShipDestination(ship);
            }

            Debug.Assert(!areHaliteBasedMapsDirty);
            var currentOutboundMap = GetOutboundMap(MapSetKind.Default);
            while (shipQueue.Count != 0)
            {
                var newOutboundMap = GetOutboundMap(MapSetKind.Default);
                bool haliteChanged = (newOutboundMap != currentOutboundMap);
                currentOutboundMap = newOutboundMap;
                shipTurnOrderComparer.OutboundMap = currentOutboundMap;

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

                if (bestShip.Destination.HasValue)
                {
                    // I thought that for outbounds it would be beneficial to convert one step earlier, because then the superior
                    // harvest logic would kick in sooner, but testing shows otherwise. Let's test it again when I have better
                    // inspiration handling.
                    int newDistanceFromDestination = mapBooster.Distance(bestShip.Position, bestShip.Destination.Value);
                    if (bestShip.Role == ShipRole.Outbound && newDistanceFromDestination == 0)
                    {
                        // Doing this early so that someone starting to harvest nearby doesn't prompt this ship to keep going unnecessarily.
                        // No similar problem with harvesters, as those are not affected by simulated halite changes very much.
                        SetShipRole(bestShip, ShipRole.Harvester);
                        UpdateShipDestination(bestShip);
                    }

                    if (bestShip.Role == ShipRole.Inbound && newDistanceFromDestination == 0)
                    {
                        // With this the inbound handler code doesn't need a special case for a ship that got pushed away from a dropoff.
                        SetShipRole(bestShip, ShipRole.Outbound);
                    }
                }

                if (bestShip.Role == ShipRole.Harvester)
                {
                    double jobTime = currentOutboundMap.GetEstimatedJobTimeInNeighbourhood(bestShip.Position, 0, bestShip.IsEarlyGameShip);
                    if (jobTime > 0)
                    {
                        meanHarvesterJobTime = (meanHarvesterJobTime * harvesterJobsAssignedCount + jobTime) / (harvesterJobsAssignedCount + 1);
                        harvesterJobsAssignedCount++;

                        logger.LogDebug(bestShip + ": job time = " + jobTime + ", mean = " + meanHarvesterJobTime + ", count = " + harvesterJobsAssignedCount);
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

        private void TurnShipsIntoLemmings()
        {

        }

        private MyShip FindBestBuilder(bool considerOnlyNotAlreadyBuilders = false)
        {
            Debug.Assert(expansionMap.BestDropoffAreaCandidates.Count > 0);
            double bestValue = double.MinValue;
            MyShip bestShip = null;
            foreach (var ship in myPlayer.MyShips)
            {
                if (considerOnlyNotAlreadyBuilders && ship.Role == ShipRole.Builder)
                {
                    continue;
                }

                double value = expansionMap.Paths[ship.Position];
                if (value > bestValue)
                {
                    bestValue = value;
                    bestShip = ship;
                }
            }

            return bestShip;
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
                            if (ship.DetourTurnCount == 0)
                            {
                                outboundMap = originOutboundMap;
                            }
                            else
                            {
                                outboundMap = originDetourOutboundMap;
                            }
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
                            if (ship.DetourTurnCount == 0)
                            {
                                outboundMap = GetOutboundMap(MapSetKind.Default);
                            }
                            else
                            {
                                outboundMap = GetOutboundMap(MapSetKind.Detour);
                            }
                            //logger.LogDebug(ship + " gets GetOutboundMap() (isEarly = " + outboundMap.IsEarlyGameMap + ").");
                        }
                    }

                    ship.Map = outboundMap.OutboundPaths;
                    ship.MapDirection = -1;
                    break;

                case ShipRole.Harvester:
                    ship.Map = originAdjustedHaliteMap.Values;
                    ship.MapDirection = 1;
                    break;

                case ShipRole.Inbound:
                    var inboundMap = (ship.DetourTurnCount == 0) ? originReturnMap.PathCosts : originDetourReturnMap.PathCosts;
                    ship.Map = inboundMap;
                    ship.MapDirection = -1;
                    break;

                case ShipRole.Builder:
                    ship.Map = expansionMap.Paths;
                    ship.MapDirection = 1;
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
            Debug.Assert(ship.Role != role);
            logger.LogDebug(ship + " changes role to " + role + ".");
            if (ship.IsEarlyGameShip && role == ShipRole.Inbound)
            {
                ship.IsEarlyGameShip = false;
                earlyGameShipCount--;
            }

            if (ship.Role == ShipRole.Builder)
            {
                bool removed = builderList.Remove(ship);
                Debug.Assert(removed);
            }
            else if (role == ShipRole.Builder)
            {
                Debug.Assert(!builderList.Contains(ship));
                builderList.Add(ship);
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

                case ShipRole.Builder:
                    TryAssignOrderToBuilder(ship);
                    break;

                default:
                    Debug.Fail("Unexpected ship role.");
                    ProcessShipOrder(ship, ship.OriginPosition);
                    break;
            }
        }

        private void TryAssignOrderToBuilder(MyShip ship)
        {
            Debug.Assert(expansionMap.BestDropoffAreaCandidates.Count > 0);

            if (GoAwayFromOpponentDropoffIfNeeded(ship))
            {
                return;
            }

            var neighbourhoodInfo = DiscoverNeighbourhood(ship, null);
            if (neighbourhoodInfo.BestValue == 0)
            {
                SetShipRole(ship, ShipRole.Outbound);
                return;
            }

            var bestBuilder = FindBestBuilder(true);
            if (bestBuilder != null)
            {
                double bestBuilderValue = expansionMap.Paths[bestBuilder.Position];
                if (bestBuilderValue > neighbourhoodInfo.BestValue)
                {
                    SetShipRole(ship, ShipRole.Outbound);
                    SetShipRole(bestBuilder, ShipRole.Builder);
                    return;
                }
            }

            if (neighbourhoodInfo.BestAllowedPosition == ship.OriginPosition)
            {
                if (neighbourhoodInfo.BestAllowedPosition == neighbourhoodInfo.BestPosition)
                {
                    int haliteAvailableLocally = ship.Halite + originHaliteMap[ship.OriginPosition];
                    int additionalHaliteNeeded = Math.Max(GameConstants.DropoffCost - haliteAvailableLocally, 0);
                    if (myPlayer.Halite >= additionalHaliteNeeded)
                    {
                        myPlayer.BuildDropoff(ship);
                    }
                    else
                    {
                        ProcessShipOrder(ship, ship.OriginPosition, false);
                    }

                    return;
                }

                AssignOrderToBlockedShip(ship, neighbourhoodInfo);
                return;
            }

            ProcessShipOrder(ship, neighbourhoodInfo.BestAllowedPosition);
        }

        private void TryAssignOrderToFugitiveShip(MyShip ship)
        {
            if (GoAwayFromOpponentDropoffIfNeeded(ship))
            {
                return;
            }

            var neighbourhoodInfo = DiscoverNeighbourhood(ship, null);
            ProcessShipOrder(ship, neighbourhoodInfo.BestAllowedPosition);
        }

        private bool GoAwayFromOpponentDropoffIfNeeded(MyShip ship)
        {
            Debug.Assert(!ship.HasActionAssigned);
            if (!permanentForbiddenCellsMap[ship.OriginPosition])
            {
                return false;
            }

            AssignOrderToBlockedShip(ship, null);
            return true;
        }

        private void TryAssignOrderToInboundShip(MyShip ship)
        {
            Debug.Assert(myPlayer.DistanceFromDropoffMap[ship.OriginPosition] != 0);

            if (GoAwayFromOpponentDropoffIfNeeded(ship))
            {
                return;
            }

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
                int minHaliteToReturn = tuningSettings.EarlyGameShipMinReturnedHalite + haliteLostOnTheWay + 5;
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
                double jobTime = originOutboundMap.GetEstimatedJobTimeInNeighbourhood(ship.OriginPosition, ship.Halite, ship.IsEarlyGameShip);
                if (jobTime > 0)
                {
                    if (meanHarvesterJobTime != 0)
                    {
                        double jobTimeRatio = jobTime / meanHarvesterJobTime;
                        bool isPointlessToHarvest = (jobTimeRatio >= tuningSettings.HarvesterToOutboundConversionMinJobTimeRatio);
                        if (isPointlessToHarvest)
                        {
                            ship.HasFoundTooLittleHaliteToHarvestThisTurn = true;
                            var newRole = (ship.Halite <= tuningSettings.HarvesterMaximumFillForTurningOutbound) ? ShipRole.Outbound : ShipRole.Inbound;
                            logger.LogInfo("Ship " + ship.Id + " at " + ship.OriginPosition + "changes role from " + ShipRole.Harvester + " to " + newRole + " because there's not enough halite here (job time = " + jobTime + ", mean = " + meanHarvesterJobTime + ", adjusted halite = " + neighbourhoodInfo.BestValue + ").");
                            SetShipRole(ship, newRole);
                            return;
                        }
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
                int halite = (int)originAdjustedHaliteMap.Values[position];
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
                int remainingAfterTurnLimit = (int)(Math.Pow(1 - GameConstants.ExtractRatio, turnLimit) * originHalite);
                int inShipAfterTurnLimit = (int)(ship.Halite + (originHalite - remainingAfterTurnLimit));
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
            if (GoAwayFromOpponentDropoffIfNeeded(ship))
            {
                return;
            }

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

            Debug.Assert(neighbourhoodInfo.OriginValue < 0, "Negative job time (" + -1 * neighbourhoodInfo.OriginValue + ") found for " + ship + ".");

            if (neighbourhoodInfo.BestAllowedPosition == ship.OriginPosition)
            {
                bool isBlocked = neighbourhoodInfo.BestValue > neighbourhoodInfo.BestAllowedValue;
                if (isBlocked)
                {
                    bool hasArrived = false;
                    double originAdjustedHalite = originAdjustedHaliteMap.Values[ship.OriginPosition];
                    if (ship.Destination.HasValue)
                    {
                        double destinationAdjustedHalite = originAdjustedHaliteMap.Values[ship.Destination.Value];
                        if (destinationAdjustedHalite != 0)
                        {
                            double haliteRatio = originAdjustedHalite / destinationAdjustedHalite;
                            hasArrived = (haliteRatio >= tuningSettings.OutboundShipToHarvesterConversionMinimumHaliteRatio);
                            if (hasArrived)
                            {
                                logger.LogDebug("Outbound " + ship + " is blocked, but finds this place good enough (OAH=" + originAdjustedHalite + ", DAH=" + destinationAdjustedHalite + ", ratio=" + haliteRatio + ") and starts harvesting.");
                            }
                        }
                        else
                        {
                            hasArrived = true;
                        }
                    }

                    if (!hasArrived)
                    {
                        AssignOrderToBlockedShip(ship, neighbourhoodInfo);
                        return;
                    }
                }

                logger.LogDebug("Outbound ship " + ship.Id + " at " + ship.OriginPosition + " starts harvesting (path value = " + neighbourhoodInfo.OriginValue + ", adjusted halite = " + originAdjustedHaliteMap.ValuesMinusReturnCost[ship.OriginPosition] + ", isBlocked = " + isBlocked + ").");
                SetShipRole(ship, ShipRole.Harvester);
                return;
            }

            ProcessShipOrder(ship, neighbourhoodInfo.BestAllowedPosition);
        }

        private void AssignOrderToBlockedShip(MyShip ship, NeighbourhoodInfo neighbourhoodInfo)
        {
            Debug.Assert(!ship.HasActionAssigned);

            if (permanentForbiddenCellsMap[ship.OriginPosition])
            {
                int originOpponentDropoffDistance = allOpponentDropoffDistanceMap[ship.OriginPosition];
                foreach (var neighbour in mapBooster.GetNeighbours(ship.OriginPosition))
                {
                    int neighbourOpponentDropoffDistance = allOpponentDropoffDistanceMap[neighbour];
                    if (neighbourOpponentDropoffDistance <= originOpponentDropoffDistance)
                    {
                        continue;
                    }

                    if (myPlayer.MyShipMap[neighbour] != null)
                    {
                        continue;
                    }

                    foreach (var opponentShip in opponentPlayers.SelectMany(player => player.OpponentShips))
                    {
                        if (opponentShip.PossibleNextPositions.Contains(neighbour))
                        {
                            continue;
                        }
                    }

                    ProcessShipOrder(ship, neighbour, false);
                    return;
                }
            }

            var desiredNeighbour = neighbourhoodInfo.BestPosition;
            bool isBlockedByOpponent = (originForbiddenCellsMap[desiredNeighbour] && myPlayer.GetFromMyShipMap(desiredNeighbour) == null);
            if (ship.DetourTurnCount > 0 && desiredNeighbour == ship.OriginPosition)
            {
                ProcessShipOrder(ship, ship.OriginPosition, true);
                return;
            }

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
                    if (GetFromAllOpponentShipMap(desiredNeighbour) == null)
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

            if (isBlockedByOpponent &&
                (ship.Role == ShipRole.Inbound || ship.Role == ShipRole.Outbound))
            {
                if (ship.DetourTurnCount == 0)
                {
                    ship.DetourTurnCount = tuningSettings.DetourTurnCount;
                    return;
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
                            if (HarvesterWantsToMoveTo(ship, originAdjustedHaliteMap.Values[ship.OriginPosition], position, originAdjustedHaliteMap.Values[position]))
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

            logger.LogDebug("Ship " + ship.Id + " at " + ship.OriginPosition + ", with role " + ship.Role + ", got ordered to " + position + " (isBlocked = " + isBlocked + ", destination = " + ship.Destination + ").");

            if (position != ship.OriginPosition)
            {
                var blocker = myPlayer.GetFromMyShipMap(position);
                if (blocker != null)
                {
                    Debug.Assert(blocker.PushPath != null && blocker.PushPath.Count >= 3 && blocker.PushPath.Last() == ship.OriginPosition, "ship = " + ship + ", blocker = " + blocker + ", push path = " + string.Join(" <- ", blocker.PushPath));
                    var pushPath = blocker.PushPath;
                    bool isSwitch = (pushPath.Peek() == ship.OriginPosition);
                    Debug.Assert(!isSwitch || pushPath.Count == 3);
                    logger.LogDebug(ship + " pushes to " + position + " (" + string.Join(" <- ", pushPath) + ").");

                    Position pushFrom;
                    Position pushTo = pushPath.Pop();
                    Debug.Assert(pushTo == ship.OriginPosition || myPlayer.GetFromMyShipMap(pushTo) == null, "Push-to position has unexpected ship (" + myPlayer.GetFromMyShipMap(pushTo) + ".");
                    while (pushPath.Count >= 2)
                    {
                        pushFrom = pushPath.Pop();
                        var pushedShip = myPlayer.GetFromMyShipMap(pushFrom);
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
            ship.SetPosition(position);
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
            opponentPlayers = new OpponentPlayer[gameInitializationMessage.Players.Length - 1];
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
                    player = new OpponentPlayer();
                    opponentPlayers[opponentIndex] = player as OpponentPlayer;
                    opponentIndex++;
                }

                player.Logger = logger;
                player.Initialize(playerInitializationMessage, gameInitializationMessage.MapWithHaliteAmounts);
            }

            originHaliteMap = new DataMapLayer<int>(gameInitializationMessage.MapWithHaliteAmounts);
            mapWidth = originHaliteMap.Width;
            mapHeight = originHaliteMap.Height;
            foreach (var position in originHaliteMap.AllPositions)
            {
                int halite = originHaliteMap[position];
                totalHaliteOnMap += halite;
            }

            totalTurnCount = 300 + 25 * (mapWidth / 8);

            mapBooster = new MapBooster(mapWidth, mapHeight, tuningSettings);
            forbiddenCellsMap = new BitMapLayer(mapWidth, mapHeight);
            permanentForbiddenCellsMap = new BitMapLayer(mapWidth, mapHeight);
            shipTurnOrderComparer = new InversePriorityShipTurnOrderComparer(originHaliteMap);
            allOpponentShipMap = new DataMapLayer<OpponentShip>(mapWidth, mapHeight);
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

            simulator = new GameSimulator()
            {
                HaliteMap = originHaliteMap,
                Logger = logger,
                MapBooster = mapBooster,
                MyPlayer = myPlayer,
                Opponents = opponentPlayers,
                TotalTurns = totalTurnCount,
                TuningSettings = tuningSettings
            };

            simulator.Initialize();

            expansionMap = new ExpansionMap()
            {
                HaliteMap = originHaliteMap,
                Logger = logger,
                MapBooster = mapBooster,
                MyPlayer = myPlayer,
                Opponents = opponentPlayers,
                TuningSettings = tuningSettings
            };

            expansionMap.Initialize();

            macroEngine = new MacroEngine()
            {
                ExpansionMap = expansionMap,
                Logger = logger,
                MapBooster = mapBooster,
                MyPlayer = myPlayer,
                Simulator = simulator,
                TotalTurns = totalTurnCount,
                TuningSettings = tuningSettings
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
            CollectIntelOnOpponentShips();
            UpdateForbiddenCellsMap();

            originReturnMap = GetReturnMap(MapSetKind.Default);
            originAdjustedHaliteMap = GetAdjustedHaliteMap(MapSetKind.Default);
            originOutboundMap = GetOutboundMap(MapSetKind.Default);
            earlyGameOriginOutboundMap = (myPlayer.Ships.Count <= 1 || earlyGameShipCount != 0) ? GetOutboundMap(MapSetKind.EarlyGame) : null;
            originDetourReturnMap = GetReturnMap(MapSetKind.Detour);
            originDetourAdjustedHaliteMap = GetAdjustedHaliteMap(MapSetKind.Detour);
            originDetourOutboundMap = GetOutboundMap(MapSetKind.Detour);

            /*PaintMap(expansionMap.CoarseHaliteMaps[0], "chm1" + TurnNumber.ToString().PadLeft(3, '0'));
            PaintMap(expansionMap.CoarseHaliteMaps[1], "chm2" + TurnNumber.ToString().PadLeft(3, '0'));
            PaintMap(expansionMap.CoarseShipCountMaps[0], "csc1" + TurnNumber.ToString().PadLeft(3, '0'));
            PaintMap(expansionMap.CoarseShipCountMaps[1], "csc2" + TurnNumber.ToString().PadLeft(3, '0'));*/
            //PaintMap(expansionMap.Paths, "expansionMap" + TurnNumber.ToString().PadLeft(3, '0'));

            /*PaintMap(allOpponentDropoffDistanceMap, TurnNumber.ToString().PadLeft(3, '0') + "allOpponentDropoffDistanceMap");
            PaintMap(originAdjustedHaliteMap.Values, TurnNumber.ToString().PadLeft(3, '0') + "AdjustedHaliteMap");
            PaintMap(originReturnMap.PathCosts, TurnNumber.ToString().PadLeft(3, '0') + "ReturnMap");
            PaintMap(originOutboundMap.OutboundPaths, TurnNumber.ToString().PadLeft(3, '0') + "OutboundMap");
            PaintMap(originOutboundMap.HarvestTimeMap, TurnNumber.ToString().PadLeft(3, '0') + "OutboundHarvestTimeMap");*/

            /*PaintMap(haliteMap, TurnNumber.ToString().PadLeft(3, '0') + "HaliteMap");
            PaintMap(originAdjustedHaliteMap.Values, TurnNumber.ToString().PadLeft(3, '0') + "AdjustedHaliteMap");
            PaintMap(originReturnMap.PathCosts, TurnNumber.ToString().PadLeft(3, '0') + "ReturnPathCosts");
            PaintMap(originOutboundMap.HarvestTimeMap, TurnNumber.ToString().PadLeft(3, '0') + "HarvestTimeMap", 250);
            PaintMap(originOutboundMap.OutboundPaths, TurnNumber.ToString().PadLeft(3, '0') + "OutboundPaths");*/

            logger.LogInfo("Turn " + TurnNumber + ": Halite on map " + totalHaliteOnMap + ", halite returned " + myPlayer.TotalReturnedHalite + ", ship count " + myPlayer.Ships.Count + ", ships sunk this turn " + myPlayer.Shipwrecks.Count + ".");

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

                foreach (var ship in player.OpponentShips)
                {
                    Debug.Assert(allOpponentShipMap[ship.Position] == null);
                    allOpponentShipMap[ship.Position] = ship;
                }
            }

            allOpponentDropoffDistanceMap = new DataMapLayer<int>(opponentPlayers[0].DistanceFromDropoffMap);
            for (int i = 1; i < opponentPlayers.Length; i++)
            {
                var opponentDropoffDistanceMap = opponentPlayers[i].DistanceFromDropoffMap;
                foreach (var position in allOpponentDropoffDistanceMap.AllPositions)
                {
                    int priorDistance = allOpponentDropoffDistanceMap[position];
                    int opponentDistance = opponentDropoffDistanceMap[position];
                    if (opponentDistance < priorDistance)
                    {
                        allOpponentDropoffDistanceMap[position] = opponentDistance;
                    }
                }
            }

            foreach (MyShip shipwreck in myPlayer.Shipwrecks)
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

                if (shipwreck.Role == ShipRole.Builder)
                {
                    bool removed = builderList.Remove(shipwreck);
                    Debug.Assert(removed);
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
                        var shipAtPosition = myPlayer.GetFromMyShipMap(position);
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
                var blocker = myPlayer.GetFromMyShipMap(position);
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
                //logger.LogDebug("AdjustHaliteForExtraction: p=" + ship.Position + ", extracted=" + extracted + ", before=" + haliteMap[ship.Position] + ", after=" + halite);
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
            Position position;
            if (ship.Role == ShipRole.Harvester)
            {
                position = ship.Destination ?? ship.OriginPosition;
            }
            else
            {
                if (!ship.Destination.HasValue)
                {
                    return;
                }

                position = ship.Destination.Value;
            }

            logger.LogDebug("Simulating harvest for " + ship + " at " + position + ".");
            //logger.LogDebug("Halite before at " + ship.Destination.Value + " is " + haliteMap[ship.Destination.Value] + " (original was " + originHaliteMap[ship.Destination.Value] + ")");
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

                int extractedAmount = Math.Min(extractableAmount, availableCapacity);
                localHalite -= extractedAmount;
                //logger.LogDebug("Simulated harvest at " + position + " from " + haliteMap[position] + " to " + (localHalite) + ".");
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

            /*if (TurnNumber >= 143 && TurnNumber < 150)
            {
                string prefix = "_" + TurnNumber + "_" + (myPlayer.Ships.Count - shipQueue.Count).ToString().PadLeft(3, '0');
                logger.LogDebug("-- Painting map series " + prefix);
                PaintMap(GetAdjustedHaliteMap(MapSetKind.Default).Values, prefix + "AHM");
                PaintMap(GetOutboundMap(MapSetKind.Default).HarvestTimeMap, prefix + "HTM");
                PaintMap(GetOutboundMap(MapSetKind.Default).OutboundPaths, prefix + "OP");
            }*/
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
                    ForbiddenCellsMap = forbiddenCellsMapToUse,
                    AllOpponentDropoffDistanceMap = allOpponentDropoffDistanceMap,
                    Opponents = opponentPlayers
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
                    return GetOutboundMap(ref dangerousOutboundMap, false, GetAdjustedHaliteMap(kind), permanentForbiddenCellsMap, GetReturnMap(MapSetKind.Default));
                case MapSetKind.Detour:
                    return GetOutboundMap(ref dangerousDetourOutboundMap, false, GetAdjustedHaliteMap(kind), originForbiddenCellsMap, GetReturnMap(MapSetKind.Detour));
                case MapSetKind.EarlyGame:
                    return GetOutboundMap(ref dangerousEarlyGameOutboundMap, true, GetAdjustedHaliteMap(MapSetKind.Default), permanentForbiddenCellsMap, GetReturnMap(MapSetKind.Default));
                default:
                    throw new ArgumentException();
            }
        }

        private OutboundMap GetOutboundMap(ref OutboundMap mapStorage, bool isEarlyGameMap, AdjustedHaliteMap adjustedHaliteMapToUse, BitMapLayer forbiddenCellsMapToUse, ReturnMap returnMapToUse)
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
                    IsEarlyGameMap = isEarlyGameMap,
                    ReturnMap = returnMapToUse,
                    AllOpponentDropoffDistanceMap = allOpponentDropoffDistanceMap,
                    Opponents = opponentPlayers
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

                if (haliteDifference < 0 && GetFromAllOpponentShipMap(position) != null)
                {
                    opponentHarvestPositionList.Add(position);
                }
            }

            opponentHarvestAreaMap.Update(opponentHarvestPositionList);

            haliteMap = new DataMapLayer<int>(originHaliteMap);
            ResetHaliteDependentState();
            //PaintMap(opponentHarvestAreaMap.HaliteMultiplierMap, "HaliteMultiplierMap" + TurnNumber);
        }

        // TODO: Improve by calculating a forbidden map for the opponents too. Then I don't have to always add the current positions
        // to the possibilities.
        private void CollectIntelOnOpponentShips()
        {
            foreach (var player in opponentPlayers)
            {
                foreach (var ship in player.OpponentShips)
                {
                    int moveCost = (int)Math.Floor(originHaliteMap[ship.Position] * GameConstants.MoveCostRatio);
                    if (ship.Halite < moveCost)
                    {
                        ship.IsOutOfFuel = true;
                    }

                    int dropoffDistance = player.DistanceFromDropoffMap[ship.Position];
                    int previousDropoffDistance = player.DistanceFromDropoffMap[ship.PreviousPosition];
                    int originHalite = originHaliteMap[ship.Position];
                    int previousHalite = originHaliteMap[ship.PreviousPosition];
                    double haliteRatio = (originHalite != 0) ? previousHalite / (double)originHalite : double.MaxValue;

                    int turnsRemaining = totalTurnCount - TurnNumber;
                    bool couldMoveAwayFromDropoff = false;
                    Position bestPreviousNeighbourHalitePosition;
                    int bestPreviousNeighbourHalite = -1;
                    foreach (var neighbour in mapBooster.GetNeighbours(ship.PreviousPosition))
                    {
                        var shipAtNeighbourNow = GetFromAllOpponentShipMap(neighbour) ?? (Ship)myPlayer.GetFromMyShipMap(neighbour);
                        if (shipAtNeighbourNow != null && shipAtNeighbourNow != ship)
                        {
                            // Meaning the opponent had reason to believe this cell to be forbidden.
                            continue;
                        }

                        int neighbourDropoffDistance = player.DistanceFromDropoffMap[neighbour];
                        if (neighbourDropoffDistance > previousDropoffDistance)
                        {
                            couldMoveAwayFromDropoff = true;
                        }

                        int neighbourHalite = originHaliteMap[neighbour];
                        if (neighbourHalite > bestPreviousNeighbourHalite)
                        {
                            bestPreviousNeighbourHalitePosition = neighbour;
                            bestPreviousNeighbourHalite = neighbourHalite;
                        }
                    }

                    double? previousNeighbourHaliteRatio = null;
                    if (bestPreviousNeighbourHalite != -1)
                    {
                        previousNeighbourHaliteRatio = (bestPreviousNeighbourHalite != 0) ? previousHalite / (double)bestPreviousNeighbourHalite : double.MaxValue;
                    }

                    if (!ship.AssumedRole.HasValue
                        && (dropoffDistance >= turnsRemaining - 2))
                    {
                        ship.AssumedRole = ShipRole.Inbound;
                    }

                    if (!ship.AssumedRole.HasValue
                        && (dropoffDistance == 0))
                    {
                        ship.AssumedRole = ShipRole.Outbound;
                    }

                    if (!ship.AssumedRole.HasValue
                        && (ship.Halite > tuningSettings.OpponentShipCertainlyInboundMinHalite
                            || (ship.Halite > tuningSettings.OpponentShipLikelyInboundMinHalite
                                && dropoffDistance < previousDropoffDistance
                                && haliteRatio > tuningSettings.OpponentHarvesterMoveThresholdHaliteRatio)))
                    {
                        ship.AssumedRole = ShipRole.Inbound;
                    }

                    if (!ship.AssumedRole.HasValue
                        && (ship.Halite < tuningSettings.OpponentShipLikelyInboundMinHalite
                            && ship.PreviousPosition == ship.Position
                            && !ship.WasOutOfFuelLastTurn
                            && couldMoveAwayFromDropoff))
                    {
                        ship.AssumedRole = ShipRole.Harvester;
                    }

                    if (!ship.AssumedRole.HasValue
                        && (dropoffDistance > previousDropoffDistance
                            && haliteRatio > tuningSettings.OpponentHarvesterMoveThresholdHaliteRatio))
                    {
                        ship.AssumedRole = ShipRole.Outbound;
                    }

                    if (!ship.AssumedRole.HasValue
                        && (ship.Halite >= tuningSettings.OpponentShipLikelyHarvesterMinHalite
                            && ship.Halite <= tuningSettings.OpponentShipLikelyInboundMinHalite))
                    {
                        ship.AssumedRole = ShipRole.Harvester;
                    }

                    bool nextPositionsSet = false;
                    var neighbours = mapBooster.GetNeighbours(ship.Position);
                    if (ship.IsOutOfFuel)
                    {
                        ship.ExpectedNextPosition = ship.Position;
                        ship.ExpectedNextPositionCertainty = 1d;
                        ship.PossibleNextPositions.Add(ship.Position);
                        nextPositionsSet = true;
                    }

                    if (!nextPositionsSet && ship.AssumedRole.HasValue)
                    {
                        switch (ship.AssumedRole.Value)
                        {
                            case ShipRole.Outbound:
                                var awayFromDropoffNeighbours = neighbours.Where(position => player.DistanceFromDropoffMap[position] > dropoffDistance);
                                ship.PossibleNextPositions.AddRange(awayFromDropoffNeighbours);
                                ship.PossibleNextPositions.Add(ship.Position);
                                nextPositionsSet = true;
                                break;

                            case ShipRole.Harvester:
                                ship.PossibleNextPositions.Add(ship.Position);

                                int maxHalite = -1;
                                Position maxHalitePosition = default(Position);
                                int secondMaxHalite = -1;
                                foreach (var position in neighbours)
                                {
                                    int neighbourHalite = originHaliteMap[position];
                                    if (neighbourHalite != 0)
                                    {
                                        double neighbourHaliteRatio = originHalite / (double)neighbourHalite;
                                        if (neighbourHaliteRatio <= tuningSettings.OpponentHarvesterMoveThresholdHaliteRatio)
                                        {
                                            ship.PossibleNextPositions.Add(position);
                                        }
                                    }

                                    if (neighbourHalite > maxHalite)
                                    {
                                        secondMaxHalite = maxHalite;
                                        maxHalite = neighbourHalite;
                                        maxHalitePosition = position;
                                    }
                                    else if (neighbourHalite > secondMaxHalite)
                                    {
                                        secondMaxHalite = neighbourHalite;
                                    }
                                }

                                if (secondMaxHalite > 0)
                                {
                                    Debug.Assert(maxHalitePosition != default(Position));
                                    Debug.Assert(secondMaxHalite != 0);
                                    ship.ExpectedNextPosition = maxHalitePosition;
                                    ship.ExpectedNextPositionCertainty = maxHalite / (double)(secondMaxHalite + maxHalite);
                                }

                                nextPositionsSet = true;
                                break;

                            case ShipRole.Inbound:
                                var towardsFromDropoffNeighbours = neighbours.Where(position => player.DistanceFromDropoffMap[position] < dropoffDistance);
                                ship.PossibleNextPositions.AddRange(towardsFromDropoffNeighbours);
                                ship.PossibleNextPositions.Add(ship.Position);

                                int minHalite = int.MaxValue;
                                Position minHalitePosition = default(Position);
                                int secondMinHalite = int.MaxValue;
                                foreach (var position in towardsFromDropoffNeighbours)
                                {
                                    int neighbourHalite = originHaliteMap[position];
                                    if (neighbourHalite < minHalite)
                                    {
                                        secondMinHalite = minHalite;
                                        minHalite = neighbourHalite;
                                        minHalitePosition = position;
                                    }
                                    else if (neighbourHalite < secondMinHalite)
                                    {
                                        secondMinHalite = neighbourHalite;
                                    }
                                }

                                if (secondMinHalite < int.MaxValue && minHalite > 0)
                                {
                                    Debug.Assert(minHalitePosition != default(Position));
                                    ship.ExpectedNextPosition = minHalitePosition;
                                    ship.ExpectedNextPositionCertainty = secondMinHalite / (double)(minHalite + secondMinHalite);
                                }

                                nextPositionsSet = true;
                                break;

                            default:
                                break;
                        }
                    }

                    if (!nextPositionsSet)
                    {
                        ship.PossibleNextPositions.AddRange(neighbours);
                        ship.PossibleNextPositions.Add(ship.Position);
                        nextPositionsSet = true;
                    }
                }
            }
        }

        private void UpdateForbiddenCellsMap()
        {
            forbiddenCellsMap.Clear();

            var noGoDisc = new Position[permanentForbiddenCellsMap.GetDiscArea(tuningSettings.MapOpponentDropoffNoGoZoneRadius)];
            var myDistanceFromEstablishedDropoffMap = new DataMapLayer<int>(mapWidth, mapHeight);
            Player.UpdateDropoffDistances(myPlayer.Dropoffs, myDistanceFromEstablishedDropoffMap, tuningSettings.MapOpponentShipInvisibilityMinDropoffAge);
            foreach (var player in opponentPlayers)
            {
                foreach (var dropoff in player.Dropoffs)
                {
                    permanentForbiddenCellsMap.GetDiscCells(dropoff.Position, tuningSettings.MapOpponentDropoffNoGoZoneRadius, noGoDisc);
                    foreach (var position in noGoDisc)
                    {
                        forbiddenCellsMap[position] = true;
                        permanentForbiddenCellsMap[position] = true;
                    }
                }

                foreach (var ship in player.OpponentShips)
                {
                    var shipPosition = ship.Position;
                    int shipMyDropoffdistance = myDistanceFromEstablishedDropoffMap[shipPosition];
                    if (shipMyDropoffdistance <= tuningSettings.MapOpponentShipInvisibilityRadius)
                    {
                        logger.LogDebug(ship + " came too close (" + shipMyDropoffdistance + ") and thus became invisible.");
                        continue;
                    }

                    logger.LogDebug("Opponent ship intel: " + ship.ToString());

                    foreach (var position in ship.PossibleNextPositions)
                    {
                        forbiddenCellsMap[position] = true;
                    }
                }
            }

            originForbiddenCellsMap = new BitMapLayer(forbiddenCellsMap);

            /*var lala = new DataMapLayer<int>(originForbiddenCellsMap.Width, originForbiddenCellsMap.Height);
            foreach (var position in originForbiddenCellsMap.AllPositions)
            {
                lala[position] = originForbiddenCellsMap[position] ? 100 : 0;
            }

            PaintMap(lala, "originForbiddenCellsMap" + TurnNumber.ToString().PadLeft(3, '0'));*/
        }

        private OpponentShip GetFromAllOpponentShipMap(Position position)
        {
            var ship = allOpponentShipMap[position];
            Debug.Assert(ship == null || ship.Position == position);
            Debug.Assert(ship == null || (ship.Owner as OpponentPlayer).GetFromOpponentShipMap(position) == ship);
            return ship;
        }

        //[Conditional("DEBUG")]
        public void PaintMap(BitMapLayer map, string name)
        {
            var intMap = new DataMapLayer<int>(map.Width, map.Height);
            foreach (var position in map.AllPositions)
            {
                intMap[position] = (map[position]) ? 100 : 0;
            }

            PaintMap(intMap, name);
        }

        //[Conditional("DEBUG")]
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

        private void PaintMap(MapLayer<double> map, string name, double maxValue)
        {
            if (IsMuted)
            {
                return;
            }

            string svg = painter.MapLayerToSvg(map, maxValue);
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
