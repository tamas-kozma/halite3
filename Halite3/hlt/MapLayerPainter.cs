namespace Halite3.hlt
{
    using System;
    using System.Text;

    public sealed class MapLayerPainter
    {
        public int CellPixelSize { get; set; } = 1;

        public string MapLayerToSvg(MapLayer<int> map)
        {
            int maxValue = 1;
            foreach (var value in map)
            {
                if (value > maxValue)
                {
                    maxValue = value;
                }
            }

            return MapLayerToSvg(map, maxValue);
        }

        public string MapLayerToSvg(MapLayer<int> map, int maxValue)
        {
            if (maxValue < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxValue));
            }

            var builder = new StringBuilder();
            int imageWidth = map.Width * CellPixelSize;
            int imageHeight = map.Height * CellPixelSize;
            builder.AppendLine("<?xml version=\"1.0\" standalone=\"no\"?>");
            builder.AppendLine("<svg width=\"" + imageWidth + "\" height=\"" + imageHeight + "\" version=\"1.1\" xmlns=\"http://www.w3.org/2000/svg\">");
            foreach (var position in map.AllPositions)
            {
                int colorIntensity = (int)(((map.GetAt(position) * 256d) / maxValue));
                int x = position.Column * CellPixelSize;
                int y = position.Row * CellPixelSize;
                builder.AppendLine("\t<rect x=\"" + x + "\" y=\"" + y + "\" width=\"" + CellPixelSize + "\" height=\"" + CellPixelSize + "\" stroke-width=\"0\" fill=\"rgb(" + colorIntensity + ", " + colorIntensity + ", " + colorIntensity + ")\"/>");
            }

            builder.AppendLine("</svg>");
            return builder.ToString();
        }
    }
}
