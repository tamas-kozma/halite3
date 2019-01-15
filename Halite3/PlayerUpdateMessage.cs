namespace Halite3
{
    public sealed class PlayerUpdateMessage
    {
        public string PlayerId { get; set; }
        public int Halite { get; set; }
        public ShipMessage[] Ships { get; set; }
        public DropoffMessage[] Dropoffs { get; set; }
    }
}
