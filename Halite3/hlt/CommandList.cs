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

        public void PopulateFromPlayer(MyPlayer player)
        {
            if (player.NewShip != null)
            {
                SpawnShip();
            }

            foreach (var ship in player.Ships)
            {
                Move(ship.Id, DirectionFromPositions(ship.OriginPosition, ship.Position));
            }
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

        private Direction DirectionFromPositions(Position origin, Position position)
        {
            if (origin == position)
            {
                return Direction.None;
            }
            else if (origin.Row != position.Row)
            {
                int delta = position.Row - origin.Row;
                if (delta == -1 || delta > 1)
                {
                    return Direction.North;
                }
                else
                {
                    return Direction.South;
                }
            }
            else
            {
                int delta = position.Column - origin.Column;
                if (delta == -1 || delta > 1)
                {
                    return Direction.West;
                }
                else
                {
                    return Direction.East;
                }
            }
        }
    }
}
