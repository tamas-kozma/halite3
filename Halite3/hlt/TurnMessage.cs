namespace Halite3.hlt
{
    public sealed class TurnMessage
    {
        public int TurnNumber { get; set; }
        public PlayerUpdateMessage[] PlayerUpdates { get; set; }
        public MapCellHaliteUpdateMessage[] MapUpdates { get; set; }
    }
}
