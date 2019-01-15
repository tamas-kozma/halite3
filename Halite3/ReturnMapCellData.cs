namespace Halite3
{
    public struct ReturnMapCellData
    {
        public readonly int Distance;
        public readonly int SumHalite;

        public ReturnMapCellData(int distance, int sumHalite)
        {
            Distance = distance;
            SumHalite = sumHalite;
        }

        public override string ToString()
        {
            return "[ Distance = " + Distance + ", SumHalite = " + SumHalite + " ]";
        }
    }
}
