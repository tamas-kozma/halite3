namespace Halite3
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

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
        public DataMapLayer<double> CurrentValues;
        public DataMapLayer<int> NextInspirerCountMap;
        public BitMapLayer NextInspirationMap;
        public DataMapLayer<double> NextValues;

        public void Calculate()
        {
            CurrentValues = new DataMapLayer<double>(MapBooster.MapWidth, MapBooster.MapHeight);
            foreach (var position in CurrentValues.AllPositions)
            {

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
