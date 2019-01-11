namespace Halite3.hlt
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;

    public class DoublePriorityQueue<TValue> : ICollection<TValue>
    {
        private Node[] list;
        private int count;
        private int capacity;

        public DoublePriorityQueue()
            : this(1024)
        {
        }

        public DoublePriorityQueue(int capacity)
        {
            this.capacity = capacity;
            list = new Node[this.capacity];
        }

        public int Count
        {
            get { return count; }
        }

        public int Capacity
        {
            get { return capacity; }
            set { capacity = value; }
        }

        bool ICollection<TValue>.IsReadOnly
        {
            get { return false; }
        }

        public void Enqueue(double priority, TValue value)
        {
            if (capacity == count)
            {
                capacity *= 2;
                var newList = new Node[capacity];
                list.CopyTo(newList, 0);
                list = newList;
            }

            list[count] = new Node(priority, value);
            count++;
            BubbleUp(count - 1);
        }

        public TValue Dequeue()
        {
            TValue result = list[0].Value;
            if (count > 1)
            {
                list[0] = list[count - 1];
            }

            //list.RemoveAt(count - 1);
            count--;

            if (count > 1)
            {
                SiftDown(0);
            }

            return result;
        }

        public TValue Peek()
        {
            return list[0].Value;
        }

        public double PeekPriority()
        {
            return list[0].Priority;
        }

        public void Clear()
        {
            count = 0;
        }

        public void TrimExcess()
        {
            throw new NotImplementedException();
        }

        public TValue[] ToArray()
        {
            return list
                .OrderBy(n => n.Priority, Comparer<double>.Default)
                .Select(n => n.Value)
                .ToArray();
        }

        public void CopyTo(TValue[] array)
        {
            CopyTo(array, 0, count);
        }

        public void CopyTo(TValue[] array, int arrayIndex)
        {
            CopyTo(array, arrayIndex, count);
        }

        public void CopyTo(TValue[] array, int arrayIndex, int count)
        {
            if (array == null)
            {
                throw new ArgumentNullException("array");
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException("count");
            }

            if (count > this.count)
            {
                count = this.count;
            }

            if (arrayIndex < 0 || arrayIndex >= array.Length)
            {
                throw new ArgumentOutOfRangeException("arrayIndex");
            }

            if (array.Length - arrayIndex < count)
            {
                throw new ArgumentException();
            }

            var valuesToCopy = list
                .OrderBy(n => n.Priority, Comparer<double>.Default)
                .Take(count)
                .Select(n => n.Value);

            int i = arrayIndex;
            foreach (TValue value in valuesToCopy)
            {
                array[i] = value;
                i++;
            }
        }

        public bool Contains(TValue value)
        {
            for (int i = 0; i < count; i++)
            {
                if (object.Equals(list[i].Value, value))
                {
                    return true;
                }
            }
            return false;
        }

        public bool ContainsPriority(double priority)
        {
            for (int i = 0; i < count; i++)
            {
                if (object.Equals(list[i].Priority, priority))
                {
                    return true;
                }
            }
            return false;
        }

        public bool Remove(TValue value)
        {
            throw new NotImplementedException();
        }

        public bool RemoveAll(TValue value)
        {
            throw new NotImplementedException();
        }

        public bool RemoveWithPriority(double priority)
        {
            throw new NotImplementedException();
        }

        public bool RemoveAllWithPriority(double priority)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<double> GetPriorities()
        {
            foreach (var node in list)
            {
                yield return node.Priority;
            }
        }

        public IEnumerator<TValue> GetEnumerator()
        {
            for (int i = 0; i < count; i++)
            {
                yield return list[i].Value;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        void ICollection<TValue>.Add(TValue item)
        {
            Enqueue(default(double), item);
        }

        private static int ParentIndex(int index)
        {
            return (index - 1) >> 1;
        }

        private static int LeftChildIndex(int index)
        {
            return (index << 1) + 1;
        }

        private static int RightChildIndex(int index)
        {
            return (index << 1) + 2;
        }

        private int LesserChildIndex(int index)
        {
            int leftChildIndex = LeftChildIndex(index);
            return (list[leftChildIndex].Priority < list[leftChildIndex + 1].Priority)
                ? leftChildIndex
                : leftChildIndex + 1;
        }

        private void BubbleUp(int index)
        {
            bool hasMovedUp = false;
            var node = list[index];
            while (index > 0)
            {
                int parentIndex = ParentIndex(index);
                var parentNode = list[parentIndex];

                if (parentNode.Priority < node.Priority)
                {
                    break;
                }

                hasMovedUp = true;
                list[index] = parentNode;
                index = parentIndex;
            }

            if (hasMovedUp)
            {
                list[index] = node;
            }
        }

        private void SiftDown(int index)
        {
            if (count < 2)
            {
                return;
            }

            int maxIndex = ParentIndex(count) - 1;

            if (index <= maxIndex)
            {
                var node = list[index];
                bool hasMovedDown = false;
                while (true)
                {
                    int leftChildIndex = (index << 1) + 1;
                    var leftChild = list[leftChildIndex];
                    var rightChild = list[leftChildIndex + 1];
                    int lesserChildIndex;
                    Node lesserChild;
                    if (leftChild.Priority < rightChild.Priority)
                    {
                        lesserChildIndex = leftChildIndex;
                        lesserChild = leftChild;
                    }
                    else
                    {
                        lesserChildIndex = leftChildIndex + 1;
                        lesserChild = rightChild;
                    }

                    if (node.Priority < lesserChild.Priority)
                    {
                        break;
                    }

                    hasMovedDown = true;
                    list[index] = lesserChild;
                    index = lesserChildIndex;
                    if (index > maxIndex)
                    {
                        break;
                    }
                }

                if (!hasMovedDown)
                {
                    return;
                }

                list[index] = node;
            }

            if (index == maxIndex + 1)
            {
                int leftChildIndex = LeftChildIndex(index);
                if (leftChildIndex >= count)
                {
                    return;
                }

                if (list[leftChildIndex].Priority < list[index].Priority)
                {
                    Node temp = list[index];
                    list[index] = list[leftChildIndex];
                    list[leftChildIndex] = temp;
                }
            }
        }

        private void RemoveAt(int index)
        {
            if (index == count - 1)
            {
                //list.RemoveAt(index);
                count--;
                return;
            }

            list[index] = list[count - 1];
            //list.RemoveAt(count - 1);
            count--;
            
            if (index > 0 && list[index].Priority < list[ParentIndex(index)].Priority)
            {
                BubbleUp(index);
            }
            else
            {
                SiftDown(index);
            }
        }

        private struct Node
        {
            public readonly double Priority;
            public readonly TValue Value;

            public Node(double newPriority, TValue newValue)
            {
                Priority = newPriority;
                Value = newValue;
            }
        }
    }
}
