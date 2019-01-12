namespace Halite3.hlt
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Newtonsoft.Json;

    public sealed class HaliteEngineInterface
    {
        private readonly Logger logger;

        public HaliteEngineInterface(Logger logger)
        {
            this.logger = logger;
        }

        public bool LogAllCommunication { get; set; }
        public bool TestMode { get; set; }
        public List<string> TestModeLines { get; set; }
        public int TestModeNextLineIndex { get; set; }

        public GameInitializationMessage ReadGameInitializationMessage()
        {
            var message = new GameInitializationMessage();
            message.GameConstants = ReadConstantsDictionary();

            var tokens = ReadTokenString();
            int playerCount = tokens.ReadInteger();
            message.MyPlayerId = tokens.ReadId();

            message.Players = new PlayerInitializationMessage[playerCount];
            for (int i = 0; i < playerCount; i++)
            {
                var playerMessage = new PlayerInitializationMessage();
                tokens = ReadTokenString();
                playerMessage.PlayerId = tokens.ReadId();
                playerMessage.ShipyardPosition = ReadPosition(tokens);
                message.Players[i] = playerMessage;
            }

            tokens = ReadTokenString();
            int mapWidth = tokens.ReadInteger();
            int mapHeight = tokens.ReadInteger();
            var haliteMap = new DataMapLayer<int>(mapWidth, mapHeight);
            message.MapWithHaliteAmounts = haliteMap;
            for (int row = 0; row < mapHeight; row++)
            {
                tokens = ReadTokenString();
                for (int column = 0; column < mapWidth; column++)
                {
                    var position = new Position(row, column);
                    int amount = tokens.ReadInteger();
                    haliteMap[position] = amount;
                }
            }

            return message;
        }

        public TurnMessage ReadTurnMessage(GameInitializationMessage initializationMessage)
        {
            var message = new TurnMessage();
            var tokens = ReadTokenString();
            if (tokens.IsEmpty)
            {
                return null;
            }

            message.TurnNumber = tokens.ReadInteger();

            int playerCount = initializationMessage.Players.Length;
            message.PlayerUpdates = new PlayerUpdateMessage[playerCount];
            for (int i = 0; i < playerCount; i++)
            {
                var playerMessage = new PlayerUpdateMessage();
                tokens = ReadTokenString();
                playerMessage.PlayerId = tokens.ReadId();
                int shipCount = tokens.ReadInteger();
                int dropoffCount = tokens.ReadInteger();
                playerMessage.Dropoffs = new DropoffMessage[dropoffCount];
                playerMessage.Halite = tokens.ReadInteger();
                playerMessage.Ships = new ShipMessage[shipCount];

                for (int j = 0; j < shipCount; j++)
                {
                    var shipMessage = new ShipMessage();
                    tokens = ReadTokenString();
                    shipMessage.ShipId = tokens.ReadId();
                    shipMessage.Position = ReadPosition(tokens);
                    shipMessage.Halite = tokens.ReadInteger();
                    playerMessage.Ships[j] = shipMessage;
                }

                for (int j = 0; j < dropoffCount; j++)
                {
                    var dropoffMessage = new DropoffMessage();
                    tokens = ReadTokenString();
                    dropoffMessage.DropoffId = tokens.ReadId();
                    dropoffMessage.Position = ReadPosition(tokens);
                    playerMessage.Dropoffs[j] = dropoffMessage;
                }

                message.PlayerUpdates[i] = playerMessage;
            }

            tokens = ReadTokenString();
            int mapUpdateMessageCount = tokens.ReadInteger();
            message.MapUpdates = new MapCellHaliteUpdateMessage[mapUpdateMessageCount];
            for (int j = 0; j < mapUpdateMessageCount; j++)
            {
                var cellUpdateMessage = new MapCellHaliteUpdateMessage();
                tokens = ReadTokenString();
                cellUpdateMessage.Position = ReadPosition(tokens);
                cellUpdateMessage.Halite = tokens.ReadInteger();
                message.MapUpdates[j] = cellUpdateMessage;
            }

            return message;
        }

        private Position ReadPosition(InputTokenString tokens)
        {
            int column = tokens.ReadInteger();
            int row = tokens.ReadInteger();
            return new Position(row, column);
        }

        public void Ready(string botName)
        {
            WriteLine(botName);
        }

        public void EndTurn(CommandList commandList)
        {
            var lineBuilder = new StringBuilder();
            foreach (string command in commandList)
            {
                lineBuilder.Append(command);
                lineBuilder.Append(' ');
            }

            WriteLine(lineBuilder.ToString());
        }

        private Dictionary<string, string> ReadConstantsDictionary()
        {
            string line = ReadLine();
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(line);
        }

        private void WriteLine(string line)
        {
            if (LogAllCommunication)
            {
                logger.LogDebug("< " + line);
            }

            if (!TestMode)
            {
                Console.WriteLine(line);
            }
        }

        private InputTokenString ReadTokenString()
        {
            string line = ReadLine();
            return new InputTokenString(line);
        }

        private string ReadLine()
        {
            string line;
            if (TestMode)
            {
                if (TestModeNextLineIndex >= TestModeLines.Count)
                {
                    line = string.Empty;
                }
                else
                {
                    line = TestModeLines[TestModeNextLineIndex++];
                }
            }
            else
            {
                var builder = new StringBuilder();
                int buffer;
                for (; (buffer = Console.Read()) >= 0;)
                {
                    if (buffer == '\n')
                    {
                        break;
                    }
                    if (buffer == '\r')
                    {
                        // Ignore carriage return if on windows for manual testing.
                        continue;
                    }

                    builder.Append((char)buffer);
                }

                line = builder.ToString();
            }

            if (LogAllCommunication)
            {
                logger.LogDebug("> " + line);
            }

            return line;
        }
    }
}
