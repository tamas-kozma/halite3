namespace Halite3.hlt
{
    public sealed class TuningSettings
    {
        public double ReturnPathDistancePenaltyMultiplier { get; set; } = 0.5d;

        public double HarvestPlanningLostHaliteMultiplier { get; set; } = 1d;
        public int HarvestPlanningMinOpponentHarvesterHalite { get; set; } = 50;
        public int HarvestPlanningMaxOpponentHarvesterHalite { get; set; } = 800;
        public int HarvestPlanningOpponentHarvesterBonusRadius { get; set; } = 5;
        public double HarvestPlanningOpponentHarvesterBonusMultiplier { get; set; } = 1.1d;
    }
}
