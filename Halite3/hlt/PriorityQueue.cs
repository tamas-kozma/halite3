namespace Halite3.hlt
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Priority queue implemented with heap.
    /// </summary>
    public class PriorityQueue<TPriority, TValue> : ICollection<TValue>
    {
        private readonly List<Node> list;
        private readonly IComparer<TPriority> comparer;

        public PriorityQueue() 
            : this(null) 
        { 
        }

        public PriorityQueue(IComparer<TPriority> newComparer)
        {
            list = new List<PriorityQueue<TPriority, TValue>.Node>();
            comparer = newComparer ?? Comparer<TPriority>.Default;
        }
        
        public PriorityQueue(int capacity) 
            : this(capacity, null) 
        { 
        }

        public PriorityQueue(int capacity, IComparer<TPriority> newComparer)
        {
            list = new List<PriorityQueue<TPriority, TValue>.Node>(capacity);
            comparer = newComparer ?? Comparer<TPriority>.Default;
        }

        public PriorityQueue(ICollection<TPriority> priorities, ICollection<TValue> values) 
            : this(priorities, values, null) 
        { 
        }

        public PriorityQueue(ICollection<TPriority> priorities, ICollection<TValue> values, IComparer<TPriority> newComparer)
        {
            if (priorities == null) 
            { 
                throw new ArgumentNullException("priorities"); 
            }
            if (values == null) 
            { 
                throw new ArgumentNullException("values"); 
            }
            if (priorities.Count != values.Count) 
            { 
                throw new ArgumentException("priorities & values"); 
            }

            list = new List<PriorityQueue<TPriority, TValue>.Node>(priorities.Count);
            comparer = newComparer ?? Comparer<TPriority>.Default;
            
            IEnumerator<TPriority> priorityEnumerator = priorities.GetEnumerator();
            IEnumerator<TValue> valueEnumerator = values.GetEnumerator();
            for (int i = 0; i < list.Count; i++)
            {
                priorityEnumerator.MoveNext();
                valueEnumerator.MoveNext();

                list[i] = new PriorityQueue<TPriority, TValue>.Node(priorityEnumerator.Current, valueEnumerator.Current);
            }

            if (list.Count < 2) 
            { 
                return;
            }

            for (int i = ParentIndex(list.Count - 1); i >= 0; i--)
            {
                SiftDown(i);
            }
        }

        public int Count 
        { 
            get { return list.Count; } 
        }

        public int Capacity
        {
            get { return list.Capacity; }
            set { list.Capacity = value; }
        }

        public IComparer<TPriority> Comparer 
        { 
            get { return comparer; } 
        }

        bool ICollection<TValue>.IsReadOnly
        {
            get { return false; }
        }

        public void Enqueue(TPriority priority, TValue value)
        {
            list.Add(new Node(priority, value));
            BubbleUp(list.Count - 1);
        }

        public TValue Dequeue()
        {
            if (Count == 0) 
            { 
                throw new InvalidOperationException("The queue is empty"); 
            }

            TValue result = list[0].Value;
            if (Count > 1) 
            { 
                list[0] = list[Count - 1]; 
            }
            
            list.RemoveAt(Count - 1);
            if (Count > 1) 
            { 
                SiftDown(0); 
            }

            return result;
        }

        public TValue Peek()
        {
            if (Count == 0) 
            { 
                throw new InvalidOperationException("The queue is empty"); 
            }

            return list[0].Value;
        }

        public TPriority PeekPriority()
        {
            if (Count == 0) 
            { 
                throw new InvalidOperationException("The queue is empty"); 
            }

            return list[0].Priority;
        }

        public void Clear()
        {
            list.Clear();
        }

        public void TrimExcess()
        {
            list.TrimExcess();
        }

        public TValue[] ToArray()
        {
            return list
                .OrderBy(n => n.Priority, comparer)
                .Select(n => n.Value)
                .ToArray();
        }

        public void CopyTo(TValue[] array) 
        { 
            CopyTo(array, 0, Count); 
        }

        public void CopyTo(TValue[] array, int arrayIndex) 
        { 
            CopyTo(array, arrayIndex, Count); 
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

            if (count > Count) 
            { 
                count = Count; 
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
                .OrderBy(n => n.Priority, comparer)
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
            for (int i = 0; i < Count; i++)
            {
                if (object.Equals(list[i].Value, value)) 
                { 
                    return true; 
                }
            }
            return false;
        }

        public bool ContainsPriority(TPriority priority)
        {
            for (int i = 0; i < Count; i++)
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

        public bool RemoveWithPriority(TPriority priority)
        {
            throw new NotImplementedException();
        }

        public bool RemoveAllWithPriority(TPriority priority)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<TPriority> GetPriorities()
        {
            foreach (var node in list)
            {
                yield return node.Priority;
            }
        }

        public IEnumerator<TValue> GetEnumerator()
        {
            foreach (var node in list)
            {
                yield return node.Value;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        void ICollection<TValue>.Add(TValue item)
        {
            Enqueue(default(TPriority), item);
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
            return (IndexLessThan(leftChildIndex, leftChildIndex + 1)) 
                ? leftChildIndex 
                : leftChildIndex + 1;
        }

        private bool IndexLessThan(int leftIndex, int rightIndex)
        {
            return (comparer.Compare(list[leftIndex].Priority, list[rightIndex].Priority) < 0);
        }

        private bool LessThan(Node left, Node right)
        {
            return (comparer.Compare(left.Priority, right.Priority) < 0);
        }

        private void BubbleUp(int index)
        {
            if (index < 1) 
            { 
                return;
            }

            int parentIndex = ParentIndex(index);
            if (IndexLessThan(parentIndex, index)) 
            {
                return;
            }

            Node temp = list[index];
            do
            {
                list[index] = list[parentIndex];
                index = parentIndex;
                if (index == 0) 
                { 
                    break;
                }

                parentIndex = ParentIndex(index);
            }
            while (LessThan(temp, list[parentIndex]));
            list[index] = temp;
        }

        private void SiftDown(int index)
        {
            if (list.Count < 2) 
            { 
                return;
            }

            int maxIndex = ParentIndex(list.Count) - 1;

            int lesserChildIndex;
            if (index <= maxIndex)
            {
                lesserChildIndex = LesserChildIndex(index);
                if (IndexLessThan(index, lesserChildIndex)) 
                { 
                    return; 
                }

                Node temp = list[index];
                do
                {
                    list[index] = list[lesserChildIndex];
                    index = lesserChildIndex;
                    if (index > maxIndex) 
                    {
                        break;
                    }

                    lesserChildIndex = LesserChildIndex(index);
                }
                while (LessThan(list[lesserChildIndex], temp));
                list[index] = temp;
            }

            if (index == maxIndex + 1)
            {
                int leftChildIndex = LeftChildIndex(index);
                if (leftChildIndex >= list.Count) 
                { 
                    return;
                }

                if (IndexLessThan(leftChildIndex, index))
                {
                    Node temp = list[index];
                    list[index] = list[leftChildIndex];
                    list[leftChildIndex] = temp;
                }
            }
        }

        private void RemoveAt(int index)
        {
            if (index == Count - 1)
            {
                list.RemoveAt(index);
                return;
            }

            list[index] = list[Count - 1];
            list.RemoveAt(Count - 1);

            if (index > 0 && IndexLessThan(index, ParentIndex(index)))
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
            public readonly TPriority Priority;
            public readonly TValue Value;

            public Node(TPriority newPriority, TValue newValue)
            {
                Priority = newPriority;
                Value = newValue;
            }
        }
    }
}
