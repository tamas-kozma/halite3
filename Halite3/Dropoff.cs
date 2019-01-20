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
            get { return Age <= 0; }
        }

        public override string ToString()
        {
            return "[dropoff-" + Id + ", O=" + Owner + ", P=" + Position + ", A=" + Age + ", IS=" + IsShipyard + "]";
        }
    }
}
