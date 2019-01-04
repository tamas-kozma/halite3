namespace Halite3
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

        private string myPlayerId;

        private GameInitializationMessage gameInitializationMessage;

        public MyBot(Logger logger, Random random, HaliteEngineInterface haliteEngineInterface, TuningSettings tuningSettings)
        {
            this.logger = logger;
            this.random = random;
            this.haliteEngineInterface = haliteEngineInterface;
            this.tuningSettings = tuningSettings;
        }

        public void Play()
        {
            gameInitializationMessage = haliteEngineInterface.ReadGameInitializationMessage();
            GameConstants.PopulateFrom(gameInitializationMessage.GameConstants);
            haliteEngineInterface.Ready(Name);

            myPlayerId = gameInitializationMessage.MyPlayerId;
            while (true)
            {
                var turnMessage = haliteEngineInterface.ReadTurnMessage(gameInitializationMessage);
                if (turnMessage == null)
                {
                    return;
                }

                if (gameInitializationMessage.MyPlayerId == "0" && turnMessage.TurnNumber == 1)
                {
                    var myPlayerUpdateMessage = turnMessage.PlayerUpdates.Single(message => message.PlayerId == myPlayerId);
                    var myPlayerInitializationMessage = gameInitializationMessage.Players.Single(message => message.PlayerId == myPlayerId);
                    var returnMap = new ReturnMap()
                    {
                        HaliteMap = gameInitializationMessage.MapWithHaliteAmounts,
                        MyPlayerInitializationMessage = myPlayerInitializationMessage,
                        MyPlayerUpdateMessage = myPlayerUpdateMessage,
                        TuningSettings = tuningSettings,
                        Logger = logger
                    };

                    returnMap.Calculate();

                    var harvestPlanningMap = new HarvestPlanningMap()
                    {
                        BaseHaliteMap = gameInitializationMessage.MapWithHaliteAmounts,
                        MyPlayerId = gameInitializationMessage.MyPlayerId,
                        TurnMessage = turnMessage,
                        ReturnMap = returnMap,
                        TuningSettings = tuningSettings,
                        Logger = logger
                    };

                    harvestPlanningMap.Calculate();
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
    }
}
