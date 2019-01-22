namespace Halite3
{
    public sealed class HarvestMap
    {
        public TuningSettings TuningSettings;
        public Logger Logger;
        public MapBooster MapBooster;
        public MyPlayer MyPlayer;
        public OpponentPlayer[] Opponents;
        public DataMapLayer<int> HaliteMap;

        public DataMapLayer<int> CurrentInspirerCountMap;
        public BitMapLayer CurrentInspirationMap;
        public DataMapLayer<double> CurrentInspiredHaliteMap;
        public DataMapLayer<double> CurrentValues;
        public DataMapLayer<int> NextInspirerCountMap;
        public BitMapLayer NextInspirationMap;
        public DataMapLayer<double> NextValues;
        public DataMapLayer<double> NextInspiredHaliteMap;

        public void Calculate()
        {
            CalculateInspirationMaps();

            CurrentInspiredHaliteMap = new DataMapLayer<double>(MapBooster.MapWidth, MapBooster.MapHeight);
            NextInspiredHaliteMap = new DataMapLayer<double>(MapBooster.MapWidth, MapBooster.MapHeight);
            foreach (var position in CurrentInspiredHaliteMap.AllPositions)
            {
                double halite = HaliteMap[position];
                CurrentInspiredHaliteMap[position] = (CurrentInspirationMap[position]) ? halite * GameConstants.InspiredBonusMultiplier : halite;
                NextInspiredHaliteMap[position] = (NextInspirationMap[position]) ? halite * GameConstants.InspiredBonusMultiplier : halite;
            }

            CurrentValues = new DataMapLayer<double>(MapBooster.MapWidth, MapBooster.MapHeight);
            NextValues = new DataMapLayer<double>(MapBooster.MapWidth, MapBooster.MapHeight);
            foreach (var position in CurrentValues.AllPositions)
            {
                double originHalite = CurrentInspiredHaliteMap[position];
                double sumNeighbourhoodHalite = 0;
                foreach (var neighbour in MapBooster.GetNeighbours(position))
                {
                    sumNeighbourhoodHalite += CurrentInspiredHaliteMap[neighbour];
                }

                CurrentValues[position] = (originHalite * 2d + sumNeighbourhoodHalite / 4d) / 3d;

                originHalite = NextInspiredHaliteMap[position];
                sumNeighbourhoodHalite = 0;
                foreach (var neighbour in MapBooster.GetNeighbours(position))
                {
                    sumNeighbourhoodHalite += NextInspiredHaliteMap[neighbour];
                }

                NextValues[position] = (originHalite * 2d + sumNeighbourhoodHalite / 4d) / 3d;
            }
        }

        private void CalculateInspirationMaps()
        {
            CurrentInspirerCountMap = new DataMapLayer<int>(MapBooster.MapWidth, MapBooster.MapHeight);
            NextInspirerCountMap = new DataMapLayer<int>(MapBooster.MapWidth, MapBooster.MapHeight);
            var inspirationDisc = new Position[HaliteMap.GetDiscArea(GameConstants.InspirationRadius)];
            foreach (var opponent in Opponents)
            {
                foreach (var ship in opponent.OpponentShips)
                {
                    HaliteMap.GetDiscCells(ship.Position, GameConstants.InspirationRadius, inspirationDisc);
                    foreach (var position in inspirationDisc)
                    {
                        CurrentInspirerCountMap[position]++;
                    }

                    var nextShipPosition = (ship.ExpectedNextPosition.HasValue) ? ship.ExpectedNextPosition.Value : ship.Position;
                    if (nextShipPosition != ship.Position)
                    {
                        HaliteMap.GetDiscCells(nextShipPosition, GameConstants.InspirationRadius, inspirationDisc);
                    }

                    foreach (var position in inspirationDisc)
                    {
                        NextInspirerCountMap[position]++;
                    }
                }
            }

            int inspirationShipCount = GameConstants.InspirationShipCount;
            CurrentInspirationMap = new BitMapLayer(MapBooster.MapWidth, MapBooster.MapHeight);
            NextInspirationMap = new BitMapLayer(MapBooster.MapWidth, MapBooster.MapHeight);
            foreach (var position in CurrentInspirationMap.AllPositions)
            {
                CurrentInspirationMap[position] = CurrentInspirerCountMap[position] >= inspirationShipCount;
                NextInspirationMap[position] = NextInspirerCountMap[position] >= inspirationShipCount;
            }
        }
    }
}
