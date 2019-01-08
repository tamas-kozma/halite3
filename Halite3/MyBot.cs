﻿namespace Halite3
{
    using Halite3.hlt;
    using System;
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
        private MyPlayer myPlayer;
        private DataMapLayer<int> haliteMap;

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

            haliteMap = new DataMapLayer<int>(gameInitializationMessage.MapWithHaliteAmounts);

            haliteEngineInterface.Ready(Name);

            while (true)
            {
                var turnMessage = haliteEngineInterface.ReadTurnMessage(gameInitializationMessage);
                if (turnMessage == null)
                {
                    return;
                }

                myPlayer.Update(turnMessage);
                UpdateHaliteMap(turnMessage);

                if (turnMessage.TurnNumber == 1)
                {
                    var returnMap = new ReturnMap()
                    {
                        HaliteMap = haliteMap,
                        TuningSettings = tuningSettings,
                        Logger = logger,
                        MyPlayer = myPlayer
                    };

                    returnMap.Calculate();

                    var outboundMap = new OutboundMap()
                    {
                        BaseHaliteMap = haliteMap,
                        GameInitializationMessage = gameInitializationMessage,
                        TurnMessage = turnMessage,
                        ReturnMap = returnMap,
                        TuningSettings = tuningSettings,
                        Logger = logger
                    };

                    outboundMap.Calculate();

                    PaintMap(haliteMap, "haliteMap");
                    PaintMap(returnMap.PathCosts, "returnPathCosts");
                    PaintMap(outboundMap.AdjustedHaliteMap, "outboundAdjustedHaliteMap");
                    PaintMap(outboundMap.DiscAverageLayer, "outboundAdjustedAverageHaliteMap");
                    PaintMap(outboundMap.HarvestAreaMap, "outboundHarvestAreas");
                    PaintMap(outboundMap.OutboundPathCellValues, "outboundCellValues");
                    PaintMap(outboundMap.OutboundPaths, "outboundPaths");
                }

                var commands = new CommandList();
                haliteEngineInterface.EndTurn(commands);
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
            if (args.Length > 1)
            {
                randomSeed = int.Parse(args[1]);
            }
            else
            {
                randomSeed = DateTime.Now.Millisecond;
            }

            var random = new Random(randomSeed);

            var engineInterface = new HaliteEngineInterface(logger);
            engineInterface.LogAllCommunication = true;

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

        private void UpdateHaliteMap(TurnMessage turnMessage)
        {
            foreach (var cellUpdateMessage in turnMessage.MapUpdates)
            {
                haliteMap[cellUpdateMessage.Position] = cellUpdateMessage.Halite;
            }
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
