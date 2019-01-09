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
        private BitMapLayer forbiddenCellsMap;
        

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

                int moveCost = (int)Math.Floor(haliteMap[ship.OriginPosition] * GameConstants.MoveCostRatio);
                if (ship.Halite < moveCost)
                {
                    ProcessShipOrder(ship, ship.OriginPosition, ship.OriginPosition);
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
            var neighbourArray = new Position[4];

            ASSIGN_ORDER_TO_SHIP_TOP:
            if (ship.Role == ShipRole.Outbound)
            {
                var outboundMap = GetOutboundMap();
                var paths = outboundMap.OutboundPaths;
                var shipMap = myPlayer.ShipMap;

                originHaliteMap.GetNeighbours(ship.OriginPosition, neighbourArray);
                double maxPathValue = paths[ship.OriginPosition];
                var maxPathValuePosition = ship.OriginPosition;
                foreach (var candidatePosition in neighbourArray)
                {
                    double pathValue = paths[candidatePosition];
                    if (pathValue <= maxPathValue)
                    {
                        continue;
                    }

                    if (forbiddenCellsMap[candidatePosition])
                    {
                        continue;
                    }

                    maxPathValue = pathValue;
                    maxPathValuePosition = candidatePosition;
                }

                if (maxPathValuePosition != ship.OriginPosition)
                {
                    var otherShip = shipMap[maxPathValuePosition];
                    if (otherShip != null)
                    {
                        Debug.Assert(!otherShip.HasActionAssigned, "Otherwise it would not be there, or the cell would be forbidden.");

                        if (otherShip.Role == ship.Role)
                        {
                            // TODO: Bug here
                            // Maybe an enemy that came close? That is in any case not handled.
                            // replay-20190109-090506+0100-1547021092-64-64.hlt
                            Debug.Assert(forbiddenCellsMap[ship.OriginPosition] == false);
                            forbiddenCellsMap[ship.OriginPosition] = true;
                            AssignOrderToShip(otherShip);
                            forbiddenCellsMap[ship.OriginPosition] = false;

                            if (forbiddenCellsMap[maxPathValuePosition])
                            {
                                goto ASSIGN_ORDER_TO_SHIP_TOP;
                            }
                            else
                            {
                                Debug.Assert(shipMap[maxPathValuePosition] == null);
                            }
                        }
                        else
                        {
                            ProcessShipOrder(otherShip, ship.OriginPosition, ship.OriginPosition);
                        }
                    }
                }

                ProcessShipOrder(ship, maxPathValuePosition, maxPathValuePosition);
            }
        }

        private void ProcessShipOrder(MyShip ship, Position position, Position destination)
        {
            Debug.Assert(!ship.HasActionAssigned);

            ship.Position = position;
            forbiddenCellsMap[position] = true;
            ship.HasActionAssigned = true;

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
                AdjustHaliteUponExtraction(ship);
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

        private void AdjustHaliteUponExtraction(MyShip ship)
        {
            int halite = haliteMap[ship.Position];
            int extracted = Math.Min((int)Math.Ceiling(halite * GameConstants.ExtractRatio), GameConstants.ShipCapacity - ship.Halite);
            halite -= extracted;
            haliteMap[ship.Position] = halite;
            ship.Halite += extracted;
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
                    MyPlayer = myPlayer
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
                    Logger = logger
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
                    Logger = logger
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
    }
}
