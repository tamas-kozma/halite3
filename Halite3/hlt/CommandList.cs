namespace Halite3.hlt
{
    using System.Collections;
    using System.Collections.Generic;

    public sealed class CommandList : IEnumerable<string>
    {
        private readonly List<string> list;

        public CommandList()
        {
            list = new List<string>(100);
        }

        public void Move(string shipId, Direction direction)
        {
            list.Add("m " + shipId + ' ' + (char)direction);
        }

        public void StayStill(string shipId)
        {
            list.Add("m " + shipId + " o");
        }

        public void BuildDropoff(string shipId)
        {
            list.Add("c " + shipId);
        }

        public void SpawnShip()
        {
            list.Add("g");
        }

        public IEnumerator<string> GetEnumerator()
        {
            return ((IEnumerable<string>)list).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<string>)list).GetEnumerator();
        }
    }
}
