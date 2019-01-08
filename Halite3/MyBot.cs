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

        private GameInitializationMessage gameInitializationMessage;
        private TurnMessage turnMessage;
        private MyPlayer myPlayer;
        private DataMapLayer<int> originHaliteMap;
        private DataMapLayer<int> haliteMap;
        private ReturnMap dangerousReturnMap;
        private AdjustedHaliteMap dangerousAdjustedHaliteMap;
        private OutboundMap dangerousOutboundMap;
        private ShipTurnOrderComparer shipTurnOrderComparer;

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
            gameInitializationMessage = haliteEngineInterface.ReadGameInitializationMessage();
            GameConstants.PopulateFrom(gameInitializationMessage.GameConstants);

            myPlayer = new MyPlayer();
            myPlayer.Initialize(gameInitializationMessage);

            originHaliteMap = new DataMapLayer<int>(gameInitializationMessage.MapWithHaliteAmounts);

            shipTurnOrderComparer = new ShipTurnOrderComparer(originHaliteMap);

            haliteEngineInterface.Ready(Name);

            while (true)
            {
                turnMessage = haliteEngineInterface.ReadTurnMessage(gameInitializationMessage);
                if (turnMessage == null)
                {
                    return;
                }

                myPlayer.Update(turnMessage);
                UpdateHaliteMap(turnMessage);

                var turnStartTime = DateTime.Now;

                if (myPlayer.Halite > GameConstants.ShipCost 
                    && !myPlayer.Ships.Any(ship => ship.OriginPosition == myPlayer.ShipyardPosition))
                {
                    myPlayer.BuildShip();   
                }

                var candidatePositionArray = new Position[originHaliteMap.GetDiscArea(1)];

                var shipsOrdered = new List<MyShip>(myPlayer.Ships);
                shipsOrdered.Sort(shipTurnOrderComparer);
                foreach (var ship in shipsOrdered)
                {
                    Debug.Assert(!ship.HasActionAssigned);

                    int moveCost = (int)Math.Floor(haliteMap[ship.OriginPosition] * GameConstants.MoveCostRatio);
                    if (ship.Halite < moveCost)
                    {
                        ship.Position = ship.OriginPosition;
                        ship.HasActionAssigned = true;
                        AdjustHaliteUponExtraction(ship);
                        continue;
                    }
                }

                foreach (var ship in shipsOrdered)
                {
                    if (ship.HasActionAssigned)
                    {
                        continue;
                    }

                    if (ship.Role == ShipRole.Outbound)
                    {
                        var outboundMap = GetOutboundMap();
                        originHaliteMap.GetDiscCells(ship.OriginPosition, 1, candidatePositionArray);
                        double maxPathValue = 0d;
                        var maxPathValuePosition = default(Position);
                        foreach (var candidatePosition in candidatePositionArray)
                        {
                            double pathValue = outboundMap.OutboundPaths[candidatePosition];
                            if (pathValue > maxPathValue)
                            {
                                maxPathValue = pathValue;
                                maxPathValuePosition = candidatePosition;
                            }
                        }

                        ship.Position = maxPathValuePosition;
                        ship.HasActionAssigned = true;
                        AdjustHaliteUponExtraction(ship);
                    }
                }

                var turnTime = DateTime.Now - turnStartTime;
                logger.WriteMessage("Turn " + turnMessage.TurnNumber + " took " + turnTime + " to compute.");

                var commands = new CommandList();
                commands.PopulateFromPlayer(myPlayer);
                haliteEngineInterface.EndTurn(commands);
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
