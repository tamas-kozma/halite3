namespace Halite3
{
    public class Ship
    {
        public Ship(Player owner)
        {
            Owner = owner;
        }

        public Player Owner { get; private set; }
        public string Id { get; set; }
        public Position Position { get; set; }
        public int Halite { get; set; }

        public override string ToString()
        {
            return "ship-" + Id + ", P=" + Position + ", H=" + Halite + "]";
        }
    }
}
