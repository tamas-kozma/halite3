namespace Halite3
{
    using Halite3.hlt;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;

    public sealed class MyBot
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
        private DataMapLayer<int> originHaliteMap;
        private DataMapLayer<int> haliteMap;
        private ReturnMap dangerousReturnMap;
        private AdjustedHaliteMap dangerousAdjustedHaliteMap;
        private OutboundMap dangerousOutboundMap;
        private ReturnMap originReturnMap;
        private AdjustedHaliteMap originAdjustedHaliteMap;
        private OutboundMap originOutboundMap;
        private ShipTurnOrderComparer shipTurnOrderComparer;
        private BitMapLayer forbiddenCellsMap;
        private MapBooster mapBooster;

        public MyBot(Logger logger, Random random, HaliteEngineInterface haliteEngineInterface, TuningSettings tuningSettings)
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

                if (myPlayer.Id == "0")
                {
                    string turnNumberString = turnMessage.TurnNumber.ToString().PadLeft(3, '0');
                    var adjustedHaliteMap = GetAdjustedHaliteMap();
                    var outboundMap = GetOutboundMap();
                    PaintMap(adjustedHaliteMap.Values, "_" + turnNumberString + "a_adjustedHalite-before");
                    PaintMap(outboundMap.HarvestAreaMap, "_" + turnNumberString + "b_harvestAreas-before");
                    PaintMap(outboundMap.OutboundPaths, "_" + turnNumberString + "c_outboundPaths-before");
                    PaintMap(originHaliteMap, "_" + turnNumberString + "cc_originHaliteMap-before");
                }

                AssignOrdersToAllShips();

                if (myPlayer.Halite >= GameConstants.ShipCost
                    && !forbiddenCellsMap[myPlayer.ShipyardPosition])
                {
                    myPlayer.BuildShip();
                }

                if (myPlayer.Id == "0")
                {
                    string turnNumberString = turnMessage.TurnNumber.ToString().PadLeft(3, '0');
                    var adjustedHaliteMap = GetAdjustedHaliteMap();
                    var outboundMap = GetOutboundMap();
                    PaintMap(adjustedHaliteMap.Values, "_" + turnNumberString + "d_adjustedHalite-after");
                    PaintMap(outboundMap.HarvestAreaMap, "_" + turnNumberString + "e_harvestAreas-after");
                    PaintMap(outboundMap.OutboundPaths, "_" + turnNumberString + "f_outboundPaths-after");
                    PaintMap(originHaliteMap, "_" + turnNumberString + "g_originHaliteMap-after");
                }

                var turnTime = DateTime.Now - turnStartTime;
                logger.WriteMessage("Turn " + turnMessage.TurnNumber + " took " + turnTime + " to compute.");

                var commands = new CommandList();
                commands.PopulateFromPlayer(myPlayer);
                haliteEngineInterface.EndTurn(commands);
            }
        }

        private void AssignOrdersToAllShips()
        {
            var shipsOrdered = new List<MyShip>(myPlayer.Ships);
            shipsOrdered.Sort(shipTurnOrderComparer);
            foreach (var ship in shipsOrdered)
            {
                Debug.Assert(!ship.HasActionAssigned);

                int moveCost = (int)Math.Floor(originHaliteMap[ship.OriginPosition] * GameConstants.MoveCostRatio);
                if (ship.Halite < moveCost)
                {
                    logger.WriteMessage("Ship " + ship.Id + " at " + ship.OriginPosition + " has not enough halite to move (" + ship.Halite + " vs " + moveCost + ").");
                    ProcessShipOrder(ship, ship.OriginPosition);
                }
            }

            foreach (var ship in shipsOrdered)
            {
                if (ship.HasActionAssigned)
                {
                    continue;
                }

                AssignOrderToShip(ship);
            }
        }

        private void AssignOrderToShip(MyShip ship)
        {
            while (true)
            {
                logger.WriteMessage("About to assing orders to ship " + ship.Id + " at " + ship.OriginPosition + ", with role " + ship.Role + " and destination " + ship.Destination + ".");
                bool retryRequested = false;
                switch (ship.Role)
                {
                    case ShipRole.Harvester:
                        retryRequested = AssignOrderToHarvester(ship);
                        break;

                    case ShipRole.Outbound:
                        retryRequested = AssignOrderToOutboundShip(ship);
                        break;

                    case ShipRole.Inbound:
                        retryRequested = AssignOrderToInboundShip(ship);
                        break;
                }

                if (!retryRequested)
                {
                    break;
                }

                logger.WriteMessage("Ship " + ship.Id + " at " + ship.OriginPosition + ", with role " + ship.Role + ", requested retry.");
            }
        }

        private bool AssignOrderToInboundShip(MyShip ship)
        {
            ship.Destination = ship.OriginPosition;
            ProcessShipOrder(ship, ship.OriginPosition);
            return false;
        }

        private bool AssignOrderToHarvester(MyShip ship)
        {
            var adjustedHaliteMap = GetAdjustedHaliteMap();
            var outboundMap = GetOutboundMap();

            var neighbourhoodInfo = DiscoverNeighbourhood(
                ship.OriginPosition,
                adjustedHaliteMap.Values,
                (Position candidate, double candidateValue, Position bestSoFar, double bestSoFarValue) => candidateValue > bestSoFarValue);

            // Handles the case when there's too little halite left in the neighbourhood.
            double pathValueAtBestPosition = outboundMap.OutboundPaths[neighbourhoodInfo.BestPosition];
            if (pathValueAtBestPosition != 0)
            {
                double bestHaliteToPathValueRatio = neighbourhoodInfo.BestValue / pathValueAtBestPosition;
                bool isPointlessToHarvest = (bestHaliteToPathValueRatio < tuningSettings.HarvesterToOutboundConversionMaximumHaliteRatio);
                if (isPointlessToHarvest)
                {
                    ship.Role = (ship.Halite <= tuningSettings.HarvesterMaximumFillForTurningOutbound)
                        ? ShipRole.Outbound
                        : ShipRole.Inbound;

                    logger.WriteMessage("Ship " + ship.Id + " at " + ship.OriginPosition + "changes role from " + ShipRole.Harvester + " to " + ship.Role + " because there's not enough halite here (pathValueAtBestPosition = " + pathValueAtBestPosition + ", bestValue = " + neighbourhoodInfo.BestValue + ").");

                    return true;
                }
            }

            // Handles the case when the ship is not blocked and moving is better than staying.
            int availableCapacity = GameConstants.ShipCapacity - ship.Halite;
            if (neighbourhoodInfo.BestAllowedPosition != ship.OriginPosition)
            {
                bool wantsToMove = WantsToMoveTo(neighbourhoodInfo.OriginValue, neighbourhoodInfo.BestAllowedValue);
                if (wantsToMove)
                {
                    if (ShouldHarvestAt(neighbourhoodInfo.BestAllowedPosition))
                    {
                        ship.Destination = neighbourhoodInfo.BestAllowedPosition;
                        bool orderAssigned = TryProcessShipOrderHandlingShipInTheWay(ship, neighbourhoodInfo.BestAllowedPosition);
                        return !orderAssigned;
                    }
                    else
                    {
                        ship.Role = ShipRole.Inbound;
                        return true;
                    }
                }
            }

            // Handles the case when the ship is blocked.
            if (neighbourhoodInfo.BestAllowedPosition == ship.OriginPosition
                && neighbourhoodInfo.OriginValue < neighbourhoodInfo.BestValue)
            {
                bool wantsToMove = WantsToMoveTo(neighbourhoodInfo.OriginValue, neighbourhoodInfo.BestValue);
                AssignOrderToBlockedShip(ship, neighbourhoodInfo.BestPosition, neighbourhoodInfo.NullableBestAllowedNeighbourPosition);
                return false;
            }

            // What's left is the case when the ship is not blocked and staying is better than moving.
            if (ShouldHarvestAt(ship.OriginPosition))
            {
                ship.Destination = ship.OriginPosition;
                ProcessShipOrder(ship, ship.OriginPosition);
                return false;
            }
            else
            {
                ship.Role = ShipRole.Inbound;
                return true;
            }

            bool WantsToMoveTo(double originAdjustedHalite, double neighbourAdjustedHalite)
            {
                if (neighbourAdjustedHalite == 0)
                {
                    return false;
                }

                double haliteRatio = originAdjustedHalite / neighbourAdjustedHalite;
                return (haliteRatio < tuningSettings.HarvesterMoveThresholdHaliteRatio);
            }

            bool ShouldHarvestAt(Position position)
            {
                int halite = originHaliteMap[position];
                int extractableIgnoringCapacity = GetExtractedAmountIgnoringCapacity(halite);
                return (ship.Halite < tuningSettings.HarvesterMinimumFillDefault
                    || extractableIgnoringCapacity <= availableCapacity);
            }
        }

        private bool AssignOrderToOutboundShip(MyShip ship)
        {
            var adjustedHaliteMap = GetAdjustedHaliteMap();
            var outboundMap = GetOutboundMap();
            var paths = outboundMap.OutboundPaths;

            var neighbourhoodInfo = DiscoverNeighbourhood(
                ship.OriginPosition, 
                paths, 
                (Position candidate, double candidateValue, Position bestSoFar, double bestSoFarValue) 
                    => (candidateValue > bestSoFarValue
                        || (candidateValue == bestSoFarValue
                            && originHaliteMap[candidate] < originHaliteMap[bestSoFar])));

            (var optimalDestination, int optimalDestinationDistance) = FollowPath(ship.OriginPosition, paths, false);
            ship.Destination = optimalDestination;

            if (neighbourhoodInfo.BestAllowedPosition == ship.OriginPosition)
            {
                bool isBlocked = neighbourhoodInfo.BestValue > neighbourhoodInfo.BestAllowedValue;
                if (isBlocked)
                {
                    bool hasArrived = false;
                    double originAdjustedHalite = adjustedHaliteMap.Values[ship.OriginPosition];
                    if (neighbourhoodInfo.OriginValue != 0)
                    {
                        double originHaliteToPathValueRatio = originAdjustedHalite / neighbourhoodInfo.OriginValue;
                        hasArrived = (originHaliteToPathValueRatio >= tuningSettings.OutboundShipToHarvesterConversionMinimumHaliteRatio);
                    }

                    if (!hasArrived)
                    {
                        AssignOrderToBlockedShip(ship, neighbourhoodInfo.BestPosition, neighbourhoodInfo.NullableBestAllowedNeighbourPosition);
                        return false;
                    }
                }

                logger.WriteMessage("Outbound ship " + ship.Id + " at " + ship.OriginPosition + " starts harvesting (path value = " + neighbourhoodInfo.OriginValue + ", halite = " + adjustedHaliteMap.Values[ship.OriginPosition] + ", isBlocked = " + isBlocked + ").");
                ship.Role = ShipRole.Harvester;
                return true;
            }

            bool orderAssigned = TryProcessShipOrderHandlingShipInTheWay(ship, neighbourhoodInfo.BestAllowedPosition);
            return !orderAssigned;
        }

        private bool TryProcessShipOrderHandlingShipInTheWay(MyShip ship, Position targetPosition)
        {
            Debug.Assert(!ship.HasActionAssigned);
            if (targetPosition != ship.OriginPosition)
            {
                var shipMap = myPlayer.ShipMap;
                var otherShip = shipMap[targetPosition];
                if (otherShip != null)
                {
                    Debug.Assert(otherShip != ship);
                    Debug.Assert(!otherShip.HasActionAssigned, "Otherwise it would not be there, or the cell would be forbidden.");

                    if (ship.Role.IsHigherPriorityThan(otherShip.Role) || otherShip.IsHoldingTheDoor)
                    {
                        // Switching places, either because this ship has higher priority, or because asking the other ship
                        // to move would potentially lead to infinite recursion.
                        ProcessShipOrder(otherShip, ship.OriginPosition);
                    }
                    else
                    {
                        Debug.Assert(!ship.IsHoldingTheDoor);
                        ship.IsHoldingTheDoor = true;
                        AssignOrderToShip(otherShip);
                        ship.IsHoldingTheDoor = false;
                        return ship.HasActionAssigned;
                    }
                }
            }

            ProcessShipOrder(ship, targetPosition);
            return true;
        }

        // TODO: Implement. What I have here now is just temporary.
        private void AssignOrderToBlockedShip(MyShip ship, Position desiredNeighbour, Position? bestAvailableNeighbour)
        {
            var targetPosition = ship.OriginPosition;
            int originDistanceFromDropoff = myPlayer.DistanceFromDropoffMap[ship.OriginPosition];
            bool isAroundDropoff = (originDistanceFromDropoff <= 1);
            if (isAroundDropoff)
            {
                if (bestAvailableNeighbour.HasValue 
                    && myPlayer.DistanceFromDropoffMap[bestAvailableNeighbour.Value] > originDistanceFromDropoff)
                {
                    targetPosition = bestAvailableNeighbour.Value;
                }
            }

            ProcessShipOrder(ship, targetPosition, true);
        }

        private NeighbourhoodInfo DiscoverNeighbourhood(Position originPosition, DataMapLayer<double> map, Func<Position, double, Position, double, bool> isBetter)
        {
            // It is always allowed to stay put for now (later I might want to flee from bullies).
            double originValue = map[originPosition];
            var info = new NeighbourhoodInfo()
            {
                OriginValue = originValue,
                OriginPosition = originPosition,
                BestAllowedValue = originValue,
                BestAllowedPosition = originPosition,
                BestValue = originValue,
                BestPosition = originPosition,
                BestAllowedNeighbourValue = -1d,
                BestAllowedNeighbourPosition = default(Position)
            };

            var neighbourArray = mapBooster.GetNeighbours(originPosition);
            foreach (var candidatePosition in neighbourArray)
            {
                double pathValue = map[candidatePosition];
                if (forbiddenCellsMap[candidatePosition])
                {
                    UpdateIfBetter(candidatePosition, pathValue, ref info.BestPosition, ref info.BestValue);
                }
                else
                {
                    UpdateIfBetter(candidatePosition, pathValue, ref info.BestAllowedPosition, ref info.BestAllowedValue);
                    UpdateIfBetter(candidatePosition, pathValue, ref info.BestAllowedNeighbourPosition, ref info.BestAllowedNeighbourValue);
                }
            }

            return info;

            void UpdateIfBetter(Position candidate, double candidateValue, ref Position bestSoFar, ref double bestSoFarValue)
            {
                if (isBetter.Invoke(candidate, candidateValue, bestSoFar, bestSoFarValue))
                {
                    bestSoFar = candidate;
                    bestSoFarValue = candidateValue;
                }
            }
        }

        private (Position, int) FollowPath(Position start, DataMapLayer<double> map, bool isMinSearch = true)
        {
            var position = start;
            int multiplier = (isMinSearch) ? 1 : -1;
            double value = map[start] * multiplier;
            int distance = 0;
            while (true)
            {
                var neighbourArray = mapBooster.GetNeighbours(position.Row, position.Column);
                double bestNeighbourValue = double.MaxValue;
                var bestNeighbour = default(Position);
                foreach (var neighbour in neighbourArray)
                {
                    double neighbourValue = map[neighbour] * multiplier;
                    if (neighbourValue < bestNeighbourValue)
                    {
                        bestNeighbour = neighbour;
                        bestNeighbourValue = neighbourValue;
                    }
                }

                if (bestNeighbourValue >= value)
                {
                    return (bestNeighbour, distance);
                }
                else
                {
                    distance++;
                    position = bestNeighbour;
                    value = bestNeighbourValue;
                }
            }
        }

        private void ProcessShipOrder(MyShip ship, Position position, bool isBlocked = false)
        {
            Debug.Assert(!ship.HasActionAssigned);

            logger.WriteMessage("Ship " + ship.Id + " at " + ship.OriginPosition + ", with role " + ship.Role + ", got ordered to " + position + ", towards destination " + ship.Destination + " (isBlocked = " + isBlocked + ").");

            ship.Position = position;
            forbiddenCellsMap[position] = true;
            ship.HasActionAssigned = true;
            ship.BlockedTurnCount = (isBlocked) ? ship.BlockedTurnCount + 1 : 0;

            if (ship.OriginPosition != position)
            {
                var shipAtOrigin = myPlayer.ShipMap[ship.OriginPosition];
                if (shipAtOrigin == ship)
                {
                    myPlayer.ShipMap[ship.OriginPosition] = null;
                }
                else
                {
                    // Can only happen when switching places.
                    Debug.Assert(shipAtOrigin.HasActionAssigned && shipAtOrigin.OriginPosition == position);
                }

                myPlayer.ShipMap[ship.Position] = ship;
            }
            else
            {
                AdjustHaliteForExtraction(ship);
            }

            if (ship.Role == ShipRole.Outbound || ship.Role == ShipRole.Harvester)
            {
                AdjustHaliteForSimulatedHarvest(ship);
            }
        }

        private void Initialize()
        {
            gameInitializationMessage = haliteEngineInterface.ReadGameInitializationMessage();
            GameConstants.PopulateFrom(gameInitializationMessage.GameConstants);

            myPlayer = new MyPlayer();
            myPlayer.Initialize(gameInitializationMessage);

            originHaliteMap = new DataMapLayer<int>(gameInitializationMessage.MapWithHaliteAmounts);

            mapWidth = originHaliteMap.Width;
            mapHeight = originHaliteMap.Height;
            mapBooster = new MapBooster(mapWidth, mapHeight, tuningSettings);
            forbiddenCellsMap = new BitMapLayer(mapWidth, mapHeight);
            shipTurnOrderComparer = new ShipTurnOrderComparer(originHaliteMap);

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

            return true;
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
            var position = ship.Destination;
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

            ResetHaliteDependentState();
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

            var bot = new MyBot(logger, random, engineInterface, tuningSettings);
            try
            {
                bot.Play();
            }
            catch (Exception exception)
            {
                logger.WriteMessage(exception.ToString());
            }
        }

        private void ResetHaliteDependentState()
        {
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
                    MapBooster = mapBooster
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
                    MapBooster = mapBooster
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
            }

            haliteMap = new DataMapLayer<int>(originHaliteMap);

            ResetHaliteDependentState();

            originReturnMap = GetReturnMap();
            originAdjustedHaliteMap = GetAdjustedHaliteMap();
            originOutboundMap = GetOutboundMap();
        }

        private void PrintMaps()
        {
            PaintMap(originHaliteMap, "haliteMap");
            PaintMap(dangerousReturnMap.PathCosts, "returnPathCosts");
            PaintMap(dangerousAdjustedHaliteMap.Values, "outboundAdjustedHaliteMap");
            PaintMap(dangerousOutboundMap.DiscAverageLayer, "outboundAdjustedAverageHaliteMap");
            PaintMap(dangerousOutboundMap.HarvestAreaMap, "outboundHarvestAreas");
            PaintMap(dangerousOutboundMap.OutboundPaths, "outboundPaths");
        }

        private void PaintMap(MapLayer<int> map, string name)
        {
            string svg = painter.MapLayerToSvg(map);
            PrintSvg(svg, name);
        }

        private void PaintMap(MapLayer<double> map, string name)
        {
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
                get { return (BestAllowedNeighbourValue != -1); }
            }

            public Position? NullableBestAllowedNeighbourPosition
            {
                get { return (HasAllowedNeighbour) ? BestAllowedNeighbourPosition : (Position?)null; }
            }
        }
    }
}
