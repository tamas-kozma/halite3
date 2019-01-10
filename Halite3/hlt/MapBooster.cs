namespace Halite3.hlt
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public sealed class MapBooster
    {
        private readonly int mapWidth;
        private readonly int mapHeight;
        private readonly TuningSettings tuningSettings;

        private BitMapLayer calculator;
        private Position[][] neighbourhoods;
        private Position[][] outboundMapHarvestAreaSmoothingDiscs;

        public MapBooster(int mapWidth, int mapHeight, TuningSettings tuningSettings)
        {
            this.mapWidth = mapWidth;
            this.mapHeight = mapHeight;
            this.tuningSettings = tuningSettings;

            Calculate();
        }

        public int MapWidth
        {
            get { return mapWidth; }
        }

        public int MapHeight
        {
            get { return mapHeight; }
        }

        public Position[] GetOutboundMapHarvestAreaSmoothingDisc(int row, int column)
        {
            int index = calculator.PositionToArrayIndex(row, column);
            return outboundMapHarvestAreaSmoothingDiscs[index];
        }

        public Position[] GetNeighbours(int row, int column)
        {
            int index = calculator.PositionToArrayIndex(row, column);
            return neighbourhoods[index];
        }

        private void Calculate()
        {
            calculator = new BitMapLayer(this.mapWidth, this.mapHeight);

            CalculateNeighbourhoods();
            CalculateOutboundMapHarvestAreaSmoothingDiscs();
        }

        public void CalculateNeighbourhoods()
        {
            neighbourhoods = new Position[calculator.CellCount][];
            foreach (var position in calculator.AllPositions)
            {
                var neighbours = new Position[4];
                calculator.GetNeighbours(position, neighbours);
                int index = calculator.PositionToArrayIndex(position);
                neighbourhoods[index] = neighbours;
            }

        }

        private void CalculateOutboundMapHarvestAreaSmoothingDiscs()
        {
            outboundMapHarvestAreaSmoothingDiscs = new Position[calculator.CellCount][];
            int radius = tuningSettings.OutboundMapHarvestAreaSmoothingRadius;
            int discArea = calculator.GetDiscArea(radius);

            foreach (var position in calculator.AllPositions)
            {
                var discPositions = new Position[discArea];
                calculator.GetDiscCells(position, radius, discPositions);
                int index = calculator.PositionToArrayIndex(position);
                outboundMapHarvestAreaSmoothingDiscs[index] = discPositions;
            }
        }
    }
}
