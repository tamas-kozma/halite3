namespace Halite3.hlt
{
    public sealed class TuningSettings
    {
        public double ReturnPathDistancePenaltyMultiplier { get; set; } = 0.5d;

        public double OutboundMapLostHaliteMultiplier { get; set; } = 1d;
        public int OutboundMapMinOpponentHarvesterHalite { get; set; } = 50;
        public int OutboundMapMaxOpponentHarvesterHalite { get; set; } = 800;
        public int OutboundMapOpponentHarvesterBonusRadius { get; set; } = 5;
        public double OutboundMapOpponentHarvesterBonusMultiplier { get; set; } = 1.1d;
        public int OutboundMapOpponentDropoffPenaltyRadius { get; set; } = 10;
        public double OutboundMapOpponentDropoffPenaltyMultiplier { get; set; } = 0.5d;
        public double OutboundMapHarvestAreaCenterWeight { get; set; } = 1d;
        public double OutboundMapPathStepPenaltyMultiplier { get; set; } = 0.96d;
        public int OutboundMapHarvestAreaSmoothingRadius { get; set; } = 2;

        public double OutboundShipToHarvesterConversionMinimumHaliteRatio { get; set; } = 0.9d;

        public double HarvesterMoveThresholdHaliteRatio { get; set; } = 0.55d;
        public int HarvesterMinimumFillDefault { get; set; } = 950;
        public int HarvesterMaximumFillForTurningOutbound { get; set; } = 650;
        public double HarvesterToOutboundConversionMaximumHaliteRatio { get; set; } = 0.4d;
    }
}
