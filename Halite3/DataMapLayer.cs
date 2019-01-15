namespace Halite3
{
    using System;
    using System.Collections.Generic;

    public class DataMapLayer<T> : MapLayer<T>
    {
        private readonly T[] array;

        public DataMapLayer(int width, int height)
            : base(width, height)
        {
            array = new T[CellCount];
        }

        public DataMapLayer(DataMapLayer<T> original)
            : this(original.Width, original.Height)
        {
            original.array.CopyTo(array, 0);
        }

        public T this[Position position]
        {
            get
            {
                int index = position.Row * Width + position.Column;
                return array[index];
            }
            set
            {
                int index = position.Row * Width + position.Column;
                array[index] = value;
            }
        }

        public T this[int row, int column]
        {
            get
            {
                int index = PositionToArrayIndex(row, column);
                return array[index];
            }
            set
            {
                int index = PositionToArrayIndex(row, column);
                array[index] = value;
            }
        }

        public override T GetAt(Position position)
        {
            return this[position];
        }

        public override void SetAt(Position position, T value)
        {
            this[position] = value;
        }

        public override void Clear()
        {
            Array.Clear(array, 0, array.Length);
        }

        public override void Fill(T value)
        {
            Array.Fill(array, value);
            /*for (int i = 0; i < array.Length; i++)
            {
                array[i] = value;
            }*/
        }

        public override IEnumerator<T> GetEnumerator()
        {
            return ((IEnumerable<T>)array).GetEnumerator();
        }
    }
}
