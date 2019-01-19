namespace Halite3
{
    using System.Collections.Generic;
    using System.Globalization;

    public static class GameConstants
    {
        /// <summary>
        /// The cost to build a single ship.
        /// </summary>
        public static int ShipCost;

        /// <summary>
        /// The cost to build a dropoff.
        /// </summary>
        public static int DropoffCost;

        /// <summary>
        /// The maximum amount of halite a ship can carry.
        /// </summary>
        public static int ShipCapacity;

        /// <summary>
        /// The maximum number of turns a game can last. This reflects the fact
        /// that smaller maps play for fewer turns.
        /// </summary>
        public static int TotalTurnCount;

        /// <summary>
        /// EXTRACT_RATIO halite (truncated) is collected from a square per turn.
        /// </summary>
        public static double ExtractRatio;

        /// <summary>
        /// MOVE_COST_RATIO halite (truncated) is needed to move off a cell.
        /// </summary>
        public static double MoveCostRatio;

        /// <summary>
        /// Whether inspiration is enabled.
        /// </summary>
        public static bool InspirationEnabled;

        /// <summary>
        /// A ship is inspired if at least INSPIRATION_SHIP_COUNT opponent
        /// ships are within this Manhattan distance.
        /// </summary>
        public static int InspirationRadius;

        /// <summary>
        /// A ship is inspired if at least this many opponent ships are within
        /// INSPIRATION_RADIUS distance.
        /// </summary>
        public static int InspirationShipCount;

        /// <summary>
        /// An inspired ship mines X halite from a cell per turn instead.
        /// </summary>
        public static double InspiredExtractRatio;

        /// <summary>
        /// An inspired ship that removes Y halite from a cell collects X*Y additional halite.
        /// </summary>
        public static double InspiredBonusMultiplier;

        /// <summary>
        /// An inspired ship instead spends X halite to move.
        /// </summary>
        public static double InspiredMoveCostRatio;

        /// <summary>
        /// Amount of halite in the bank at game start.
        /// </summary>
        public static int InitialHalite;

        public static void PopulateFrom(Dictionary<string, string> dictionary)
        {
            ShipCost = int.Parse(dictionary["NEW_ENTITY_ENERGY_COST"], CultureInfo.InvariantCulture);
            DropoffCost = int.Parse(dictionary["DROPOFF_COST"], CultureInfo.InvariantCulture);
            ShipCapacity = int.Parse(dictionary["MAX_ENERGY"], CultureInfo.InvariantCulture);
            TotalTurnCount = int.Parse(dictionary["MAX_TURNS"], CultureInfo.InvariantCulture);
            ExtractRatio = 1d / int.Parse(dictionary["EXTRACT_RATIO"], CultureInfo.InvariantCulture);
            MoveCostRatio = 1d / int.Parse(dictionary["MOVE_COST_RATIO"], CultureInfo.InvariantCulture);
            InspirationEnabled = bool.Parse(dictionary["INSPIRATION_ENABLED"]);
            InspirationRadius = int.Parse(dictionary["INSPIRATION_RADIUS"], CultureInfo.InvariantCulture);
            InspirationShipCount = int.Parse(dictionary["INSPIRATION_SHIP_COUNT"], CultureInfo.InvariantCulture);
            InspiredExtractRatio = 1d / int.Parse(dictionary["INSPIRED_EXTRACT_RATIO"], CultureInfo.InvariantCulture);
            InspiredBonusMultiplier = double.Parse(dictionary["INSPIRED_BONUS_MULTIPLIER"], CultureInfo.InvariantCulture);
            InspiredMoveCostRatio = 1d / int.Parse(dictionary["INSPIRED_MOVE_COST_RATIO"], CultureInfo.InvariantCulture);
            InitialHalite = int.Parse(dictionary["INITIAL_ENERGY"], CultureInfo.InvariantCulture);
        }
    }
}
