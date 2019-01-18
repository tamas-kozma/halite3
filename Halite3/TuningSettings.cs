namespace Halite3
{
    public sealed class TuningSettings
    {
        public int MapOpponentShipInvisibilityRadius { get; set; } = 3;
        public int MapOpponentDropoffNoGoZoneRadius { get; set; } = 3;
        public int MapOpponentShipInvisibilityMinDropoffAge { get; set; } = 40;

        public int OpponentShipLikelyHarvesterMinHalite { get; set; } = 150;
        public int OpponentShipLikelyHarvesterMaxHalite { get; set; } = 750;
        public double OpponentShipLikelyHarvesterMoveMaxHaliteRatio { get; set; } = 1.25d;

        public double ReturnPathDistancePenaltyMultiplier { get; set; } = 0.5d;

        public double AdjustedHaliteMapLostHaliteMultiplier { get; set; } = 1d;
        public double AdjustedHaliteMapMaxHalite { get; set; } = 2000;

        // TODO
        public int OutboundMapMinOpponentHarvesterHalite { get; set; } = 50;
        public int OutboundMapMaxOpponentHarvesterHalite { get; set; } = 800;
        public double OutboundMapHarvestAreaCenterWeight { get; set; } = 1d;
        public double OutboundMapPathStepPenaltyMultiplier { get; set; } = 0.92d; // 0.96
        public double OutboundMapEarlyGamePathStepPenaltyMultiplier { get; set; } = 0.88d;
        public int OutboundMapHarvestAreaSmoothingRadius { get; set; } = 2;
        public int OutboundMapDropoffAvoidanceRadius { get; set; } = 2;

        public double OutboundShipToHarvesterConversionMinimumHaliteRatio { get; set; } = 0.85d;
        public int OutboundShipSwitchToOriginMapDistance { get; set; } = 2;
        public int OutboundShipAntiSquarePathMinDifference { get; set; } = 3;

        public double HarvesterMoveThresholdHaliteRatio { get; set; } = 0.42d;
        public double HarvesterBlockedMoveThresholdHaliteRatio { get; set; } = 0.3d;
        public int HarvesterMinimumFillDefault { get; set; } = 950;
        public int HarvesterMinimumFillWhenBlockedByOpponent { get; set; } = 700;
        public int HarvesterMaximumFillForTurningOutbound { get; set; } = 650;
        public double HarvesterToOutboundConversionMinJobTimeRatio { get; set; } = 1.5d;
        public double HarvesterAllowedOverfillRatio { get; set; } = 0.5d;

        public int FugitiveShipConversionMinBlockedTurnCount { get; set; } = 6;
        public double FugitiveShipConversionRatio { get; set; } = 0.5d;
        public int FugitiveShipMinTurnCount { get; set; } = 2;
        public int FugitiveShipMaxTurnCount { get; set; } = 4;

        public double EarlyGameTargetShipRatio { get; set; } = 0.8d;
        public bool IsEarlyGameFeatureEnabled { get; set; } = true;
        public bool IsTwoPlayerAggressiveModeEnabled { get; set; } = false;
        public int DetourTurnCount { get; set; } = 5;

        public int OpponentHarvestAreaMapMaxScentStrength { get; set; } = 20;
        public int OpponentHarvestAreaMapHaliteBonusExtraRadius { get; set; } = 0;
    }
}
