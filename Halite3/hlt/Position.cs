namespace Halite3.hlt
{
    using System;
    using System.Collections.Generic;

    public struct Position : IEquatable<Position>
    {
        private readonly int row;
        private readonly int column;

        public Position(int row, int column)
        {
            this.row = row;
            this.column = column;
        }

        public int Row
        {
            get { return row; }
        }

        public int Column
        {
            get { return column; }
        }

        public static IEnumerable<Position> GetRectangleCells(int row1, int column1, int row2, int column2)
        {
            for (int row = row1; row <= row2; row++)
            {
                for (int column = column1; column <= column2; column++)
                {
                    yield return new Position(row, column);
                }
            }
        }

        public static bool operator ==(Position left, Position right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Position left, Position right)
        {
            return !left.Equals(right);
        }

        public override bool Equals(object obj)
        {
            if (obj is Position otherPosition)
            {
                return Equals(otherPosition);
            }

            return false;
        }

        public bool Equals(Position other)
        {
            return (row == other.row && column == other.column);
        }

        public override int GetHashCode()
        {
            return (row * 37 + column);
        }

        public override string ToString()
        {
            return "(" + Row + "," + Column + ")";
        }
    }
}
