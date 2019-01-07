namespace Halite3.hlt
{
    using System;
    using System.Linq;

    public class HarvestPlanningMap
    {
        private DataMapLayer<int>[] sumLayers;
        private DataMapLayer<double> adjustedHaliteMap;
        private 

        public TuningSettings TuningSettings { get; set; }
        public DataMapLayer<int> BaseHaliteMap { get; set; }
        public string MyPlayerId { get; set; }
        public TurnMessage TurnMessage { get; set; }
        public ReturnMap ReturnMap { get; set; }
        public Logger Logger { get; set; }

        public DataMapLayer<int> HarvestPlan { get; set; }

        public void Calculate()
        {
            CalculateAdjustedHaliteMap();
            CalculateSumLayers();
        }

        private void CalculateHarvestPlan()
        {
            HarvestPlan = new DataMapLayer<int>(sumLayers[0]);
            foreach (var position in HarvestPlan.AllPositions)
            {
                var outerRectangle = new Rectangle(position.Column, position.Row, position.Column, position.Row);
                var innerRectangle = default(Rectangle);
                int cellSize = 1;
                for (int i = 0; i < sumLayers.Length; i++)
                {
                    innerRectangle = outerRectangle;
                    //outerRectangle
                    //var sumLayer = sumLayers[i];
                    //row1 -= 
                    cellSize *= 2;
                }

            }
        }

        private void CalculateAdjustedHaliteMap()
        {
            adjustedHaliteMap = new DataMapLayer<double>(BaseHaliteMap.Width, BaseHaliteMap.Height);
            foreach (var position in BaseHaliteMap.AllPositions)
            {
                int halite = BaseHaliteMap[position];
                double returnPathCost = ReturnMap.PathCosts[position];
                double lostHalite = GameConstants.MoveCostRatio * returnPathCost * TuningSettings.HarvestPlanningLostHaliteMultiplier;
                adjustedHaliteMap[position] = Math.Max(halite - lostHalite, 0);
            }

            var opponentHarvesterPositions = TurnMessage.PlayerUpdates
                .Where(message => message.PlayerId != MyPlayerId)
                .SelectMany(message => message.Ships)
                .Where(shipMessage =>
                    shipMessage.Halite > TuningSettings.HarvestPlanningMinOpponentHarvesterHalite
                    && shipMessage.Halite < TuningSettings.HarvestPlanningMaxOpponentHarvesterHalite)
                .Select(shipMessage => shipMessage.Position);

            int opponentHarvesterBonusRadius = TuningSettings.HarvestPlanningOpponentHarvesterBonusRadius;
            int radiusArea = BaseHaliteMap.GetRadiusArea(opponentHarvesterBonusRadius);
            var radiusArray = new Position[radiusArea];
            foreach (var position in opponentHarvesterPositions)
            {
                adjustedHaliteMap.GetCircleCells(position, opponentHarvesterBonusRadius, radiusArray);
                foreach (var radiusPosition in radiusArray)
                {
                    double adjustedHalite = adjustedHaliteMap[radiusPosition];
                    adjustedHalite *= TuningSettings.HarvestPlanningOpponentHarvesterBonusMultiplier;
                    adjustedHaliteMap[radiusPosition] = adjustedHalite;
                }
            }
        }

        private void CalculateSumLayers()
        {
            var baseLayer = new DataMapLayer<int>(BaseHaliteMap.Width, BaseHaliteMap.Height);
            foreach (var position in baseLayer.AllPositions)
            {
                baseLayer[position] = (int)Math.Round(adjustedHaliteMap[position]);
            }

            int longestMapDimension = Math.Max(BaseHaliteMap.Width, BaseHaliteMap.Height);
            int layerCount = (int)Math.Ceiling(Math.Log(longestMapDimension, 2));
            sumLayers = new DataMapLayer<int>[layerCount];
            sumLayers[0] = baseLayer;
            for (int level = 1; level < layerCount; level++)
            {
                var previousLayer = sumLayers[level - 1];
                int layerWidth = (int)Math.Ceiling(previousLayer.Width / 2d);
                int layerHeight = (int)Math.Ceiling(previousLayer.Height / 2d);
                var layer = new DataMapLayer<int>(layerWidth, layerHeight);
                sumLayers[level] = layer;
                foreach (var position in layer.AllPositions)
                {
                    int row1 = position.Row * 2;
                    int column1 = position.Column * 2;
                    int row2 = Math.Min(previousLayer.Height - 1, row1 + 1);
                    int column2 = Math.Min(previousLayer.Width - 1, column1 + 1);
                    var allPreviousLayerPositions = Position.GetRectangleCells(row1, column1, row2, column2);

                    int sum = 0;
                    foreach (var previousLayerPosition in allPreviousLayerPositions)
                    {
                        sum += previousLayer.GetAt(previousLayerPosition);
                    }

                    layer.SetAt(position, sum);
                }
            }
        }

        private struct RingCell
        {
            private readonly int layerIndex;
            private readonly Position position;
            private readonly int distance;

            public RingCell(Position position, int distance)
            {
                this.position = position;
                this.distance = distance;
            }

            public Position Position
            {
                get { return position; }
            }

            public int Distance
            {
                get { return distance; }
            }
        }
    }
}
