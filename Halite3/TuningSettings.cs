namespace Halite3
{
    public sealed class TuningSettings
    {
        public int MapOpponentShipInvisibilityRadius { get; set; } = 5;
        public int MapOpponentDropoffNoGoZoneRadius { get; set; } = 3;

        public int OpponentShipLikelyHarvesterMinHalite { get; set; } = 150;
        public int OpponentShipLikelyHarvesterMaxHalite { get; set; } = 750;
        public double OpponentShipLikelyHarvesterMoveMaxHaliteRatio { get; set; } = 1.25d;

        public double ReturnPathDistancePenaltyMultiplier { get; set; } = 0.5d;

        public double AdjustedHaliteMapLostHaliteMultiplier { get; set; } = 1d;

        // TODO
        public int OutboundMapMinOpponentHarvesterHalite { get; set; } = 50;
        public int OutboundMapMaxOpponentHarvesterHalite { get; set; } = 800;
        public int OutboundMapOpponentHarvesterBonusRadius { get; set; } = 5;
        public double OutboundMapOpponentHarvesterBonusMultiplier { get; set; } = 1.0d; // 1.1
        public double OutboundMapHarvestAreaCenterWeight { get; set; } = 1d;
        public double OutboundMapPathStepPenaltyMultiplier { get; set; } = 0.92d; // 0.96
        public int OutboundMapHarvestAreaSmoothingRadius { get; set; } = 2;
        public int OutboundMapDropoffAvoidanceRadius { get; set; } = 2;

        public double OutboundShipToHarvesterConversionMinimumHaliteRatio { get; set; } = 0.9d;
        public int OutboundShipSwitchToOriginMapDistance { get; set; } = 3;
        public int OutboundShipAntiSquarePathMinDifference { get; set; } = 3;

        public double HarvesterMoveThresholdHaliteRatio { get; set; } = 0.4d;
        public int HarvesterMinimumFillDefault { get; set; } = 950;
        public int HarvesterMaximumFillForTurningOutbound { get; set; } = 650;
        public double HarvesterToOutboundConversionMaximumHaliteRatio { get; set; } = 0.5d;
        public double HarvesterAllowedOverfillRatio { get; set; } = 0.5d;

        public int FugitiveShipConversionMinBlockedTurnCount { get; set; } = 5;
        public double FugitiveShipConversionRatio { get; set; } = 0.5d;
        public int FugitiveShipMinTurnCount { get; set; } = 2;
        public int FugitiveShipMaxTurnCount { get; set; } = 4;
    }
}
