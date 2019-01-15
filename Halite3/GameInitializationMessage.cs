namespace Halite3
{
    using System.Collections.Generic;

    public sealed class GameInitializationMessage
    {
        public Dictionary<string, string> GameConstants { get; set; }
        public string MyPlayerId { get; set; }
        public PlayerInitializationMessage[] Players { get; set; }
        public DataMapLayer<int> MapWithHaliteAmounts { get; set; }
    }
}
