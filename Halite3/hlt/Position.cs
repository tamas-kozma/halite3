namespace Halite3.hlt
{
    using System;
    using System.Collections.Generic;

    public struct Position : IEquatable<Position>
    {
        public readonly int Row;
        public readonly int Column;

        public Position(int row, int column)
        {
            Row = row;
            Column = column;
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
            return (Row == other.Row && Column == other.Column);
        }

        public override int GetHashCode()
        {
            return (Row * 37 + Column);
        }

        public override string ToString()
        {
            return "(" + Row + "," + Column + ")";
        }
    }
}
