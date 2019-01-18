namespace Halite3
{
    public sealed class Dropoff
    {
        public Dropoff(Player owner)
        {
            Owner = owner;
        }

        public string Id;
        public readonly Player Owner;
        public Position Position;
        public int Age;
        public bool IsShipyard;

        public bool IsPlanned
        {
            get { return Age < 0; }
        }
    }
}
