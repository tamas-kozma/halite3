namespace Halite3
{
    using System.Collections.Generic;
    using System.Diagnostics;

    public sealed class ListBank<T>
    {
        private readonly Queue<List<T>> storage;

        public ListBank()
        {
            storage = new Queue<List<T>>();
        }

        public List<T> Rent()
        {
            var list = (storage.Count == 0)
                ? new List<T>()
                : storage.Dequeue();

            return list;
        }

        public void Return(List<T> list)
        {
            Debug.Assert(list.Count == 0, "TODO: remove this later");
            list.Clear();
            storage.Enqueue(list);
        }
    }
}
