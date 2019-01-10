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
        private ShipTurnOrderComparer shipTurnOrderComparer;
        private BitMapLayer forbiddenCellsMap; // It is always allowed to stay put for now (later I might want to flee from bullies).
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

                AssignOrdersToAllShips();

                if (myPlayer.Halite >= GameConstants.ShipCost
                    && !forbiddenCellsMap[myPlayer.ShipyardPosition])
                {
                    myPlayer.BuildShip();
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
                bool retryRequested = false;
                switch (ship.Role)
                {
                    case ShipRole.Harvester:
                        retryRequested = AssignOrderToHarvester(ship);
                        break;

                    case ShipRole.Outbound:
                        retryRequested = AssignOrderToOutboundShip(ship);
                        break;
                }

                if (!retryRequested)
                {
                    break;
                }
            }
        }

        private bool AssignOrderToHarvester(MyShip ship)
        {
            var adjustedHaliteMap = GetAdjustedHaliteMap();
            var outboundMap = GetOutboundMap();

            var neighbourhoodInfo = DiscoverNeighbourhood(
                ship.OriginPosition,
                adjustedHaliteMap.Values,
                (Position candidate, double candidateValue, Position bestSoFar, double bestSoFarValue) => candidateValue > bestSoFarValue);

            double bestOutboundPathValue = outboundMap.OutboundPaths[neighbourhoodInfo.BestPosition];
            if (bestOutboundPathValue != 0)
            {
                double bestHaliteToPathValueRatio = neighbourhoodInfo.BestValue / bestOutboundPathValue;
                bool isPointlessToHarvest = (bestHaliteToPathValueRatio < tuningSettings.OutboundShipConversionMinimumHaliteRatio);
                if (isPointlessToHarvest)
                {
                    if (ship.Halite <= tuningSettings.HarvesterMaximumFillForTurningOutbound)
                    {
                        ship.Role = ShipRole.Outbound;
                    }
                    else
                    {
                        ship.Role = ShipRole.Inbound;
                    }

                    return true;
                }
            }

            int originHalite = originHaliteMap[ship.OriginPosition];
            int availableCapacity = GameConstants.ShipCapacity - ship.Halite;
            // TODO: 0s?
            double bestPositionHaliteRatio = originHalite / (double)originHaliteMap[neighbourhoodInfo.BestPosition];
            double bestAllowedPositionHaliteRatio = originHalite / (double)originHaliteMap[neighbourhoodInfo.BestAllowedPosition];
            bool isBlocked = (neighbourhoodInfo.BestAllowedPosition != ship.OriginPosition
                && true);

            if (neighbourhoodInfo.BestAllowedPosition != ship.OriginPosition)
            {

                int neighbourHalite = originHaliteMap[neighbourhoodInfo.BestAllowedPosition];
            }

            if (neighbourhoodInfo.BestAllowedPosition == ship.OriginPosition)
            {
                int originHalite = originHaliteMap[ship.OriginPosition];
                int extractableIgnoringCapacity = GetExtractedAmountIgnoringCapacity(originHalite);
                bool isReadyToReturn = (ship.Halite >= tuningSettings.HarvesterMinimumFillDefault
                    && extractableIgnoringCapacity > availableCapacity);

                if (isReadyToReturn)
                {
                    ship.Role = ShipRole.Inbound;
                    return true;
                }
            }

            ship.Destination = ship.OriginPosition;
            ProcessShipOrder(ship, ship.OriginPosition);

            return false;

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
            var shipMap = myPlayer.ShipMap;

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
                    double originAdjustedHalite = adjustedHaliteMap.Values[ship.OriginPosition];
                    if (neighbourhoodInfo.OriginValue != 0)
                    {
                        double originHaliteToPathValueRatio = originAdjustedHalite / neighbourhoodInfo.OriginValue;
                        bool hasArrived = (originHaliteToPathValueRatio >= tuningSettings.OutboundShipConversionMinimumHaliteRatio);
                        if (hasArrived)
                        {
                            ship.Role = ShipRole.Harvester;
                            return true;
                        }
                    }

                    AssignOrderToBlockedShip(ship, neighbourhoodInfo.BestPosition, neighbourhoodInfo.NullableMaxAllowedNeighbourPathValuePosition);
                    return false;
                }
            }
            else
            {
                var otherShip = shipMap[neighbourhoodInfo.BestAllowedPosition];
                if (otherShip != null)
                {
                    Debug.Assert(!otherShip.HasActionAssigned, "Otherwise it would not be there, or the cell would be forbidden.");

                    if (otherShip.Role.IsHigherOrEqualPriorityThan(ship.Role))
                    {
                        bool wasForbidden = forbiddenCellsMap[ship.OriginPosition];
                        forbiddenCellsMap[ship.OriginPosition] = true;
                        AssignOrderToShip(otherShip);
                        forbiddenCellsMap[ship.OriginPosition] = wasForbidden;

                        if (forbiddenCellsMap[neighbourhoodInfo.BestAllowedPosition])
                        {
                            return true;
                        }
                        else
                        {
                            Debug.Assert(shipMap[neighbourhoodInfo.BestAllowedPosition] == null);
                        }
                    }
                    else
                    {
                        // Switch places with lower priority ship.
                        ProcessShipOrder(otherShip, ship.OriginPosition);
                    }
                }
            }

            ProcessShipOrder(ship, neighbourhoodInfo.BestAllowedPosition);
            return false;
        }

        private void AssignOrderToBlockedShip(MyShip ship, Position desiredNeighbour, Position? bestAvailableNeighbour)
        {
            // TODO: Implement...
            ProcessShipOrder(ship, ship.OriginPosition, true);
        }

        private NeighbourhoodInfo DiscoverNeighbourhood(Position originPosition, DataMapLayer<double> map, Func<Position, double, Position, double, bool> isBetter)
        {
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
            MarkOpponentShipyardsAsForbidden();

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
            int extracted = Math.Min(GetExtractedAmountIgnoringCapacity(halite), GameConstants.ShipCapacity - ship.Halite);
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
            return Math.Max(GetExtractedAmountIgnoringCapacity(halite), availableCapacity);
        }

        private void AdjustHaliteForSimulatedHarvest(MyShip ship)
        {
            var position = ship.Destination;
            int localHalite = haliteMap[position];
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

                int availableCapacity = GameConstants.ShipCapacity - ship.Halite;
                int extractableAmount = GetExtractedAmountIgnoringCapacity(localHalite);
                if (extractableAmount == 0
                    || (ship.Halite > tuningSettings.HarvesterMinimumFillDefault 
                        && extractableAmount > availableCapacity))
                {
                    break;
                }

                int extractedAmount = Math.Min(extractableAmount, availableCapacity);
                localHalite -= extractedAmount;
                haliteMap[position] = localHalite;
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
                    HaliteMap = originHaliteMap,
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
                    BaseHaliteMap = originHaliteMap,
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

            public Position? NullableMaxAllowedNeighbourPathValuePosition
            {
                get { return (HasAllowedNeighbour) ? BestAllowedNeighbourPosition : (Position?)null; }
            }
        }
    }
}
