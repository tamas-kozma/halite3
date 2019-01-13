namespace Halite3.hlt
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;

    public abstract class MapLayer<T> : IEnumerable<T>
    {
        private readonly int halfWidth;
        private readonly int halfHeight;

        public readonly int Width;
        public readonly int Height;

        protected MapLayer(int width, int height)
        {
            Width = width;
            Height = height;

            halfWidth = Width / 2;
            halfHeight = Height / 2;
        }

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
            if (rowDistance > halfHeight)
            {
                rowDistance = Height - rowDistance;
            }

            int columnDistance = Math.Abs(position2.Column - position1.Column);
            if (columnDistance > halfWidth)
            {
                columnDistance = Width - columnDistance;
            }

            return rowDistance + columnDistance;
        }

        public int MaxSingleDimensionDistance(Position position1, Position position2)
        {
            int rowDistance = Math.Abs(position2.Row - position1.Row);
            if (rowDistance > halfHeight)
            {
                rowDistance = Height - rowDistance;
            }

            int columnDistance = Math.Abs(position2.Column - position1.Column);
            if (columnDistance > halfWidth)
            {
                columnDistance = Width - columnDistance;
            }

            return Math.Max(rowDistance, columnDistance);
        }

        public void GetNeighbours(Position position, Position[] neighbourArray)
        {
            neighbourArray[0] = new Position(position.Row, NormalizeSingleNegativeColumn(position.Column - 1));
            neighbourArray[1] = new Position(NormalizeSingleNegativeRow(position.Row - 1), position.Column);
            neighbourArray[2] = new Position(position.Row, NormalizeNonNegativeColumn(position.Column + 1));
            neighbourArray[3] = new Position(NormalizeNonNegativeRow(position.Row + 1), position.Column);
        }

        public int GetCircleCircumFerence(int radius)
        {
            return (radius != 0) ? 4 * radius : 1;
        }

        public int GetDiscArea(int radius)
        {
            return (2 * radius) * (radius + 1) + 1;
        }

        public void GetCircleCells(Position position, int radius, Position[] positionArray)
        {
            if (radius == 0)
            {
                positionArray[0] = position;
            }
            else
            {
                int index = 0;
                for (int delta1 = 0; delta1 < radius; delta1++)
                {
                    int delta2 = radius - delta1;
                    positionArray[index++] = new Position(NormalizeSingleNegativeRow(position.Row - delta1), NormalizeSingleNegativeColumn(position.Column - delta2));
                    positionArray[index++] = new Position(NormalizeSingleNegativeRow(position.Row - delta2), NormalizeNonNegativeColumn(position.Column + delta1));
                    positionArray[index++] = new Position(NormalizeNonNegativeRow(position.Row + delta1), NormalizeNonNegativeColumn(position.Column + delta2));
                    positionArray[index++] = new Position(NormalizeNonNegativeRow(position.Row + delta2), NormalizeSingleNegativeColumn(position.Column - delta1));
                }

                Debug.Assert(index == positionArray.Length);
            }
        }

        public void GetDiscCells(Position position, int radius, Position[] positionArray)
        {
            positionArray[0] = position;
            int index = 1;
            for (int delta1 = 0; delta1 < radius; delta1++)
            {
                for (int delta2 = 1; delta2 <= radius - delta1; delta2++)
                {
                    positionArray[index++] = new Position(NormalizeSingleNegativeRow(position.Row - delta1), NormalizeSingleNegativeColumn(position.Column - delta2));
                    positionArray[index++] = new Position(NormalizeSingleNegativeRow(position.Row - delta2), NormalizeNonNegativeColumn(position.Column + delta1));
                    positionArray[index++] = new Position(NormalizeNonNegativeRow(position.Row + delta1), NormalizeNonNegativeColumn(position.Column + delta2));
                    positionArray[index++] = new Position(NormalizeNonNegativeRow(position.Row + delta2), NormalizeSingleNegativeColumn(position.Column - delta1));
                }
            }

            Debug.Assert(index == positionArray.Length);
        }

        public int NormalizeNonNegativeColumn(int column)
        {
            return column % Width;
        }

        public int NormalizeNonNegativeRow(int row)
        {
            return row % Height;
        }

        public int NormalizeSingleNegativeColumn(int column)
        {
            return (column + Width) % Width;
        }

        public int NormalizeSingleNegativeRow(int row)
        {
            return (row + Height) % Height;
        }

        public int PositionToArrayIndex(Position position)
        {
            return position.Row * Width + position.Column;
        }

        public int PositionToArrayIndex(int row, int column)
        {
            return row * Width + column;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
