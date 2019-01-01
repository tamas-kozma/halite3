namespace Halite3
{
    using Halite3.hlt;
    using System;
    using System.Globalization;
    using System.IO;

    public sealed class MyBot
    {
        private const string Name = "Sotarto";

        private readonly Logger logger;
        private readonly Random random;
        private readonly HaliteEngineInterface haliteEngineInterface;

        private GameInitializationMessage gameInitializationMessage;

        public MyBot(Logger logger, Random random, HaliteEngineInterface haliteEngineInterface)
        {
            this.logger = logger;
            this.random = random;
            this.haliteEngineInterface = haliteEngineInterface;
        }

        public void Play()
        {
            gameInitializationMessage = haliteEngineInterface.ReadGameInitializationMessage();
            GameConstants.PopulateFrom(gameInitializationMessage.GameConstants);
            haliteEngineInterface.Ready(Name);

            while (true)
            {
                var turnMessage = haliteEngineInterface.ReadTurnMessage(gameInitializationMessage);
                if (turnMessage == null)
                {
                    return;
                }

                var painter = new MapLayerPainter();
                string svg = painter.MapLayerToSvg(gameInitializationMessage.MapWithHaliteAmounts);
                File.WriteAllText("haliteMap.svg", svg);

                ////
                var start = DateTime.Now;
                unchecked
                {
                    var map = gameInitializationMessage.MapWithHaliteAmounts;
                    int totalIterations = 0;
                    for (int i = 0; i < 0; i++)
                    {
                        for (int row = 0; row < map.Height; row++)
                        {
                            for (int column = 0; column < map.Width; column++)
                            {
                                totalIterations++;
                                var position = new Position(row, column);
                                int halite = map[position];
                                int distance = Math.Abs((row - column) % map.Width) + Math.Abs((column - row) % map.Height);
                                halite = (int)(Math.Pow(distance, 2) * halite / 2);
                                map[position] = halite;
                            }
                        }
                    }

                    foreach (var haliteAtCell in map)
                    {
                        //logger.WriteMessage(haliteAtCell.ToString());
                    }

                    logger.WriteMessage(totalIterations.ToString());
                }

                var elapsed = DateTime.Now - start;
                logger.WriteMessage(elapsed.ToString());
                ////

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

            var bot = new MyBot(logger, random, engineInterface);
            try
            {
                bot.Play();
            }
            catch (Exception exception)
            {
                logger.WriteMessage(exception.ToString());
            }
        }
    }
}
