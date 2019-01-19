namespace Halite3
{
    public abstract class Ship
    {
        public Ship(Player owner)
        {
            Owner = owner;
        }

        public Player Owner { get; private set; }
        public string Id { get; set; }
        public Position Position { get; set; }
        public int Halite { get; set; }
        public Position PreviousPosition { get; set; }

        protected virtual string ToStringCore()
        {
            return "ship-" + Id + ", P=" + Position + ", PP=" + PreviousPosition + ", H=" + Halite;
        }

        public sealed override string ToString()
        {
            return "[" + ToStringCore() + "]";
        }
    }
}
