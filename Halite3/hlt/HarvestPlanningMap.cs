namespace Halite3.hlt
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;

    public class HarvestPlanningMap
    {
        private DataMapLayer<int>[] sumLayers;
        private DataMapLayer<double> adjustedHaliteMap;

        public DataMapLayer<int> BaseHaliteMap { get; set; }
        public string MyPlayerId { get; set; }
        public TurnMessage TurnMessage { get; set; }

        public void Calculate()
        {
            adjustedHaliteMap = new DataMapLayer<double>(BaseHaliteMap.Width, BaseHaliteMap.Height);
            foreach (var position in BaseHaliteMap.AllPositions)
            {
                adjustedHaliteMap[position] = BaseHaliteMap[position];
            }

            var myPlayerUpdateMessage = TurnMessage.PlayerUpdates.First(message => message.PlayerId == MyPlayerId);
            foreach (var dropoffMessage in myPlayerUpdateMessage.Dropoffs)
            {
                foreach (var position in adjustedHaliteMap.AllPositions)
                {
                }
            }


            CalculateSumLayers();
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
    }
}
