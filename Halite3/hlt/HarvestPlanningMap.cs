namespace Halite3.hlt
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    public class HarvestPlanningMap
    {
        private readonly MapDataLayer<int>[] sumLayers;

        public HarvestPlanningMap(MapDataLayer<int> haliteMap)
        {
            int longestMapDimension = Math.Max(haliteMap.Width, haliteMap.Height);
            int layerCount = (int)Math.Ceiling(Math.Log(longestMapDimension, 2));
            sumLayers = new MapDataLayer<int>[layerCount];
            sumLayers[0] = new MapDataLayer<int>(haliteMap);
            for (int level = 1; level < layerCount; level++)
            {
                var previousLayer = sumLayers[level - 1];
                int layerWidth = (int)Math.Ceiling(previousLayer.Width / 2d);
                int layerHeight = (int)Math.Ceiling(previousLayer.Height / 2d);
                var layer = new MapDataLayer<int>(layerWidth, layerHeight);
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

            for (int i = 0; i < sumLayers.Length; i++)
            {
                var painter = new MapLayerPainter();
                painter.CellPixelSize = 8;
                string svg = painter.MapLayerToSvg(sumLayers[i]);
                File.WriteAllText("haliteMap-" + i + ".svg", svg);
            }
        }
    }
}
