namespace Halite3.hlt
{
    using System.Collections.Generic;

    public sealed class GameInitializationMessage
    {
        public Dictionary<string, string> GameConstants { get; set; }
        public string CurrentPlayerId { get; set; }
        public PlayerInitializationMessage[] Players { get; set; }
        public MapDataLayer<int> MapWithHaliteAmounts { get; set; }
    }
}
