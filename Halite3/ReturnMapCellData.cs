namespace Halite3
{
    public struct ReturnMapCellData
    {
        private readonly int distance;
        private readonly int sumHalite;

        public ReturnMapCellData(int distance, int sumHalite)
        {
            this.distance = distance;
            this.sumHalite = sumHalite;
        }

        public int Distance
        {
            get { return distance; }
        }

        public int SumHalite
        {
            get { return sumHalite; }
        }

        public override string ToString()
        {
            return "[ Distance = " + Distance + ", SumHalite = " + SumHalite + " ]";
        }
    }
}
