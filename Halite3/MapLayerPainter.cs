namespace Halite3
{
    using System;
    using System.Text;

    public sealed class MapLayerPainter
    {
        public int CellPixelSize { get; set; } = 1;

        public string MapLayerToSvg(MapLayer<int> map)
        {
            int maxValue = 1;
            foreach (int value in map)
            {
                if (value != int.MaxValue && value > maxValue)
                {
                    maxValue = value;
                }
            }

            return MapLayerToSvg(map, maxValue);
        }

        public string MapLayerToSvg(MapLayer<double> map)
        {
            double maxValue = 1;
            foreach (double value in map)
            {
                if (value != double.MaxValue && value > maxValue)
                {
                    maxValue = value;
                }
            }

            return MapLayerToSvg(map, maxValue);
        }

        public string MapLayerToSvg(MapLayer<double> map, double maxValue)
        {
            var intMap = new DataMapLayer<int>(map.Width, map.Height);
            foreach (var position in intMap.AllPositions)
            {
                intMap[position] = (int)Math.Round(map.GetAt(position));
            }

            int intMaxValue = (int)Math.Round(maxValue);
            return MapLayerToSvg(intMap, intMaxValue);
        }

        public string MapLayerToSvg(MapLayer<int> map, int maxValue)
        {
            if (maxValue < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxValue), "MapLayerToSvg maxValue = " + maxValue);
            }

            var builder = new StringBuilder();
            int imageWidth = map.Width * CellPixelSize;
            int imageHeight = map.Height * CellPixelSize;
            builder.AppendLine("<?xml version=\"1.0\" standalone=\"no\"?>");
            builder.AppendLine("<svg width=\"" + imageWidth + "\" height=\"" + imageHeight + "\" version=\"1.1\" xmlns=\"http://www.w3.org/2000/svg\">");
            foreach (var position in map.AllPositions)
            {
                int normalizedValue = Math.Min((int)(((map.GetAt(position) * 255d) / maxValue)), 255);
                int red = normalizedValue;
                int blue = 255 - normalizedValue;
                int green = normalizedValue;
                int x = position.Column * CellPixelSize;
                int y = position.Row * CellPixelSize;
                builder.AppendLine("\t<rect x=\"" + x + "\" y=\"" + y + "\" width=\"" + CellPixelSize + "\" height=\"" + CellPixelSize + "\" stroke-width=\"0\" fill=\"rgb(" + red + ", " + green + ", " + blue + ")\"/>");
            }

            builder.AppendLine("</svg>");
            return builder.ToString();
        }
    }
}
