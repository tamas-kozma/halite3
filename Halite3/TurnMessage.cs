namespace Halite3
{
    public sealed class TurnMessage
    {
        public int TurnNumber { get; set; }
        public PlayerUpdateMessage[] PlayerUpdates { get; set; }
        public MapCellHaliteUpdateMessage[] MapUpdates { get; set; }
    }
}
