namespace Halite3
{
    public class Ship
    {
        public string Id { get; set; }
        public Position Position { get; set; }
        public int Halite { get; set; }

        public override string ToString()
        {
            return "ship-" + Id + ", P=" + Position + ", H=" + Halite + "]";
        }
    }
}
