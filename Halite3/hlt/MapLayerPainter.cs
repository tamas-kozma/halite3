namespace Halite3.hlt
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    public sealed class MapLayerPainter
    {
        public string MapLayerToSvg(MapLayer<int> map)
        {
            int max = 1;
            foreach (var value in map)
            {
                if (value > max)
                {
                    max = value;
                }
            }

            var builder = new StringBuilder();
            builder.AppendLine("<?xml version=\"1.0\" standalone=\"no\"?>");
            builder.AppendLine("<svg width=\"" + map.Width * 4 + "\" height=\"" + map.Height * 4 + "\" version=\"1.1\" xmlns=\"http://www.w3.org/2000/svg\">");
            for (int row = 0; row < map.Height; row++)
            {
                for (int column = 0; column < map.Width; column++)
                {
                    var position = new Position(row, column);
                    int color = (int)(((double)map.GetAt(position) / (double)max) * 256d);
                    builder.AppendLine("<rect x=\"" + column * 4 + "\" y=\"" + row * 4 + "\" width=\"4\" height=\"4\" stroke=\"rgb(" + color + ", 0, 0)\" fill=\"rgb(" + color + ", 0, 0)\"/>");
                }
            }

            builder.AppendLine("</svg>");

            return builder.ToString();
        }
    }
}
