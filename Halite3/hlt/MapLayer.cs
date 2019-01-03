namespace Halite3.hlt
{
    using System;
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
        public abstract void Fill(T value);

        public abstract IEnumerator<T> GetEnumerator();

        public int WraparoundDistance(Position position1, Position position2)
        {
            int rowDistance = Math.Abs(position2.Row - position1.Row);
            if (rowDistance > Height / 2)
            {
                rowDistance = Height - rowDistance;
            }

            int columnDistance = Math.Abs(position2.Column - position1.Column);
            if (columnDistance > Width / 2)
            {
                columnDistance = Width - columnDistance;
            }

            return rowDistance + columnDistance;
        }


        public void GetNeighbours(Position position, Position[] neighbourArray)
        {
            neighbourArray[0] = new Position(position.Row, (position.Column - 1 + Width) % Width);
            neighbourArray[1] = new Position((position.Row - 1 + Height) % Height, position.Column);
            neighbourArray[2] = new Position(position.Row, (position.Column + 1) % Width);
            neighbourArray[3] = new Position((position.Row + 1) % Height, position.Column);
        }

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
