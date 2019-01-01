namespace Halite3.hlt
{
    using System;
    using System.Collections.Generic;

    public class MapDataLayer<T> : MapLayer<T>
    {
        private readonly T[] array;

        public MapDataLayer(int width, int height)
            : base(width, height)
        {
            array = new T[CellCount];
        }

        public MapDataLayer(MapDataLayer<T> original)
            : this(original.Width, original.Height)
        {
            original.array.CopyTo(array, 0);
        }

        public T this[Position position]
        {
            get
            {
                int index = PositionToArrayIndex(position);
                return array[index];
            }
            set
            {
                int index = PositionToArrayIndex(position);
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

        public override IEnumerator<T> GetEnumerator()
        {
            return ((IEnumerable<T>)array).GetEnumerator();
        }
    }
}
