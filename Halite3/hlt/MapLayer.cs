namespace Halite3.hlt
{
    using System.Collections;
    using System.Collections.Generic;

    public abstract class MapLayer<T> : IEnumerable<T>
    {
        protected MapLayer(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; private set; }
        public int Height { get; private set; }

        public int CellCount
        {
            get { return Width * Height; }
        }

        public IEnumerable<Position> AllPositions
        {
            get
            {
                return Position.GetRectangleCells(0, 0, Height - 1, Width - 1);
            }
        }

        public abstract T GetAt(Position position);
        public abstract void SetAt(Position position, T value);

        public abstract void Clear();

        public abstract IEnumerator<T> GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        protected int PositionToArrayIndex(Position position)
        {
            return position.Row * Width + position.Column;
        }
    }
}
