namespace Halite3
{
    using System.Collections;
    using System.Collections.Generic;

    public class BitMapLayer : MapLayer<bool>
    {
        private readonly BitArray bits;

        public BitMapLayer(int width, int height)
            : base(width, height)
        {
            bits = new BitArray(CellCount);
        }

        public BitMapLayer(BitMapLayer other)
            : base(other.Width, other.Height)
        {
            bits = new BitArray(other.bits);
        }

        public bool this[Position position]
        {
            get
            {
                int index = PositionToArrayIndex(position);
                return bits[index];
            }
            set
            {
                int index = PositionToArrayIndex(position);
                bits[index] = value;
            }
        }

        public override void Clear()
        {
            bits.SetAll(false);
        }

        public override void Fill(bool value)
        {
            bits.SetAll(value);
        }

        public override bool GetAt(Position position)
        {
            return this[position];
        }

        public override void SetAt(Position position, bool value)
        {
            this[position] = value;
        }

        public override IEnumerator<bool> GetEnumerator()
        {
            foreach (bool value in bits)
            {
                yield return value;
            }
        }
    }
}
