namespace Halite3
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    public sealed class GameSimulator
    {
        public TuningSettings TuningSettings;
        public DataMapLayer<int> HaliteMap;
        public MyPlayer MyPlayer;
        public OpponentPlayer[] Opponents;
        public Logger Logger;
        public MapBooster MapBooster;
        public int TotalTurns;

        public int TurnNumber;
        public int MaxDistance;
        public Player[] AllPlayers;
        public Dictionary<string, PlayerInfo> PlayerInfoMap;
        public BitMapLayer VisitedCells;

        public void Initialize()
        {
            MaxDistance = (HaliteMap.Width + HaliteMap.Height) / 2;
            VisitedCells = new BitMapLayer(HaliteMap.Width, HaliteMap.Height);

            PlayerInfoMap = new Dictionary<string, PlayerInfo>();
            int playerCount = 1 + Opponents.Length;
            AllPlayers = new Player[playerCount];
            AllPlayers[0] = MyPlayer;
            Opponents.CopyTo(AllPlayers, 1);
            foreach (var player in AllPlayers)
            {
                var cellsAtDistances = new PlayerInfo(this, player);
                PlayerInfoMap[player.Id] = cellsAtDistances;
            }
        }

        public SimulationResult RunSimulation(int turnNumber, List<PlayerEvent> eventList)
        {
            TurnNumber = turnNumber;
            VisitedCells.Clear();
            foreach (var playerInfo in PlayerInfoMap.Values)
            {
                playerInfo.Reset();
            }

            var eventQueue = new PriorityQueue<int, PlayerEvent>(eventList.Count);
            foreach (var playerEvent in eventList)
            {
                eventQueue.Enqueue(playerEvent.TurnNumber, playerEvent);
            }

            Debug.Assert(eventQueue.Count == 0 || eventQueue.Peek().TurnNumber >= TurnNumber);

            bool simulationDone = false;
            int turn = TurnNumber;
            int visitedCellCount = 0;
            int effectiveTotalTurns = TotalTurns - (MaxDistance / 4);
            if (turn < effectiveTotalTurns)
            {
                double remainingTimeRatio = (effectiveTotalTurns - TurnNumber) / (double)effectiveTotalTurns;
                double harvestRatio = remainingTimeRatio * TuningSettings.SimulatorHarvestRatioMultiplier;
                double outboundStepTime = OutboundMap.GetBaseOutboundStepTime();
                double cellCost = harvestRatio / GameConstants.ExtractRatio;
                for (; turn < effectiveTotalTurns; turn++)
                {
                    while (eventQueue.Count > 0 && eventQueue.PeekPriority() == turn)
                    {
                        var playerEvent = eventQueue.Dequeue();
                        var playerInfo = PlayerInfoMap[playerEvent.Player.Id];
                        playerInfo.HaliteAdjustments += playerEvent.HaliteChange;
                        playerInfo.ShipCount += playerEvent.ShipCountChange;
                        if (playerEvent.NewDropoffPosition.HasValue)
                        {
                            var newDropoff = new Dropoff(playerInfo.Player)
                            {
                                Age = 1,
                                Position = playerEvent.NewDropoffPosition.Value
                            };

                            Debug.Assert(!playerInfo.Dropoffs.Any(dropoff => dropoff.Position == newDropoff.Position));
                            playerInfo.Dropoffs.Add(newDropoff);
                            playerInfo.UpdateDropoffDistances();
                        }
                    }

                    foreach (var playerInfo in PlayerInfoMap.Values)
                    {
                        playerInfo.ShipTurnsLeft += playerInfo.ShipCount;
                    }

                    bool someoneHarvested = true;
                    while (someoneHarvested)
                    {
                        someoneHarvested = false;
                        foreach (var playerInfo in PlayerInfoMap.Values)
                        {
                            if (playerInfo.ShipTurnsLeft <= 0)
                            {
                                continue;
                            }

                            int distance = playerInfo.CurrentDistance;
                            int positionIndex = playerInfo.CurrentIndex;
                            int harvestedCellCount = playerInfo.HarvestedCellCount;
                            List<Position> distanceList = playerInfo.DistanceLists[distance];
                            double cost = 0;
                            double haliteInShip = 0;
                            bool firstCellFound = false;
                            while (haliteInShip < GameConstants.ShipCapacity)
                            {
                                while (positionIndex >= distanceList.Count)
                                {
                                    if (distance == MaxDistance)
                                    {
                                        simulationDone = true;
                                        break;
                                    }

                                    distance++;
                                    distanceList = playerInfo.DistanceLists[distance];
                                    positionIndex = 0;
                                }

                                if (simulationDone)
                                {
                                    break;
                                }

                                var position = distanceList[positionIndex];
                                if (!VisitedCells[position])
                                {
                                    if (!firstCellFound)
                                    {
                                        cost = distance * outboundStepTime;
                                        firstCellFound = true;
                                    }

                                    cost += cellCost + 1;
                                    haliteInShip += HaliteMap[position] * harvestRatio;
                                    visitedCellCount++;
                                    harvestedCellCount++;
                                    VisitedCells[position] = true;
                                }

                                positionIndex++;
                            }

                            if (firstCellFound)
                            {
                                cost += distance * outboundStepTime;
                                playerInfo.ShipTurnsLeft -= cost;
                                playerInfo.ShipTurnsUsed += cost;
                                playerInfo.HaliteCollected += (int)Math.Ceiling(haliteInShip);
                                someoneHarvested = true;
                            }

                            playerInfo.CurrentDistance = distance;
                            playerInfo.CurrentIndex = positionIndex;
                            playerInfo.HarvestedCellCount = harvestedCellCount;

                            if (simulationDone)
                            {
                                goto SIMULATION_DONE;
                            }
                        }
                    }
                }
            }

            SIMULATION_DONE:
            int simulationTurnCount = turn - TurnNumber;
            int remainingTurnCount = effectiveTotalTurns - turn;
            foreach (var playerInfo in PlayerInfoMap.Values)
            {
                playerInfo.ShipTurnsLeft += remainingTurnCount * playerInfo.ShipCount;
                double totalShipTurns = playerInfo.ShipTurnsUsed + playerInfo.ShipTurnsLeft;
                if (playerInfo.ShipTurnsUsed > 0)
                {
                    double shipTurnRatio = totalShipTurns / playerInfo.ShipTurnsUsed;
                    playerInfo.HaliteCollected = (int)Math.Round(playerInfo.HaliteCollected * shipTurnRatio);
                }
            }

            foreach (var playerInfo in PlayerInfoMap.Values)
            {
                int totalHaliteInShips = playerInfo.Player.Ships.Sum(ship => ship.Halite);
                playerInfo.HaliteCollected += totalHaliteInShips;
            }

            var result = new SimulationResult()
            {
                PlayerResultMap = new Dictionary<string, PlayerResult>(),
                VisitedCells = new BitMapLayer(VisitedCells),
                SimulationEndTurn = turn,
                VisitedCellCount = visitedCellCount,
                VisitedCellRatio = visitedCellCount / (double)HaliteMap.CellCount
            };

            foreach (var playerInfo in PlayerInfoMap.Values)
            {
                var playerResult = new PlayerResult()
                {
                    Player = playerInfo.Player,
                    NetHalite = playerInfo.HaliteCollected,
                    Halite = playerInfo.InitialHalite + playerInfo.HaliteAdjustments + playerInfo.HaliteCollected,
                    HarvestedCellCount = playerInfo.HarvestedCellCount
                };

                result.PlayerResultMap[playerInfo.Player.Id] = playerResult;
            }

            return result;
        }

        public class SimulationResult
        {
            public Dictionary<string, PlayerResult> PlayerResultMap;
            public BitMapLayer VisitedCells;
            public int SimulationEndTurn;
            public int VisitedCellCount;
            public double VisitedCellRatio;

            public override string ToString()
            {
                return "Simulation result: end turn=" + SimulationEndTurn + ", VisitedCellRatio=" + VisitedCellRatio + ", player results: " + string.Join(" ", PlayerResultMap.Values);
            }
        }

        public class PlayerResult
        {
            public Player Player;
            public int NetHalite;
            public int Halite;
            public int HarvestedCellCount;

            public override string ToString()
            {
                return "[" + Player + ": NetHalite=" + NetHalite + ", Halite=" + Halite + ", HarvestedCellCount=" + HarvestedCellCount + "]";
            }
        }

        public class PlayerEvent
        {
            public Player Player;
            public int TurnNumber;
            public int ShipCountChange;
            public Position? NewDropoffPosition;
            public int HaliteChange;
        }

        public class PlayerInfo
        {
            public GameSimulator Simulator;
            public readonly Player Player;
            public readonly List<Dropoff> Dropoffs;
            public readonly List<Position>[] DistanceLists;
            public DataMapLayer<int> DropoffDistanceMap;
            public int ShipCount;
            public double ShipTurnsLeft;
            public DataMapLayer<int> HaliteMap;
            public int HaliteCollected;
            public int InitialHalite;
            public int HaliteAdjustments;
            public int CurrentDistance;
            public int CurrentIndex;
            public int HarvestedCellCount;
            public double ShipTurnsUsed;

            public PlayerInfo(GameSimulator simulator, Player player)
            {
                Simulator = simulator;
                HaliteMap = Simulator.HaliteMap;
                Player = player;
                Dropoffs = new List<Dropoff>();
                DistanceLists = new List<Position>[Simulator.MaxDistance + 1];
                for (int distance = 0; distance <= Simulator.MaxDistance; distance++)
                {
                    int maxPositionCount = HaliteMap.GetCircleCircumFerence(distance) * player.Dropoffs.Count;
                    DistanceLists[distance] = new List<Position>(maxPositionCount);
                }
            }

            public void Reset()
            {
                InitialHalite = Player.Halite;
                HaliteCollected = 0;
                HaliteAdjustments = 0;
                Dropoffs.Clear();
                Dropoffs.AddRange(Player.Dropoffs);
                DropoffDistanceMap = Player.DistanceFromDropoffMap;
                ShipCount = Player.Ships.Count;
                ShipTurnsLeft = 0;
                CurrentDistance = 0;
                CurrentIndex = 0;
                HarvestedCellCount = 0;
                ShipTurnsUsed = 0;
                PopulateDistanceLists();
            }

            public void UpdateDropoffDistances()
            {
                Player.UpdateDropoffDistances(Dropoffs, DropoffDistanceMap);
                PopulateDistanceLists();
                CurrentIndex = 0;
                CurrentDistance = 0;
            }

            private void PopulateDistanceLists()
            {
                foreach (var list in DistanceLists)
                {
                    list.Clear();
                }

                foreach (var position in HaliteMap.AllPositions)
                {
                    int distance = DropoffDistanceMap[position];
                    DistanceLists[distance].Add(position);
                }
            }
        }
    }
}
