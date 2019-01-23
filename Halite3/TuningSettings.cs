using System;

namespace Halite3
{
    public sealed class TuningSettings
    {
        public int MapOpponentShipInvisibilityRadius { get; set; } = 2;
        public int MapOpponentDropoffNoGoZoneRadius { get; set; } = 3;
        public int MapOpponentShipInvisibilityMinDropoffAge { get; set; } = 10;

        public int OpponentShipLikelyHarvesterMinHalite { get; set; } = 251;
        public int OpponentShipLikelyHarvesterMaxHalite { get; set; } = 750;
        public int OpponentShipCertainlyInboundMinHalite { get; set; } = 990;
        public int OpponentShipLikelyInboundMinHalite { get; set; } = 900;
        public double OpponentHarvesterMoveThresholdHaliteRatio { get; set; } = 0.55d;

        public double ReturnPathDistancePenaltyMultiplier { get; set; } = 0.5d;

        public double AdjustedHaliteMapLostHaliteMultiplier { get; set; } = 1d;
        public double AdjustedHaliteMapMaxHalite { get; set; } = 2000;

        public double OutboundMapHarvestAreaCenterWeight { get; set; } = 2d;
        public int OutboundMapHarvestAreaSmoothingRadius { get; set; } = 2;
        public int OutboundMapDropoffAvoidanceRadius { get; set; } = 2;
        public int OutboundMapMaxSpreadDistance { get; set; } = 6;

        public double OutboundShipToHarvesterConversionMinimumHaliteRatio { get; set; } = 1d;
        public int OutboundShipSwitchToOriginMapDistance { get; set; } = 2;
        public int OutboundShipAntiSquarePathMinDifference { get; set; } = 3;

        public double HarvesterMoveThresholdHaliteRatio { get; set; } = 0.35d;
        public double HarvesterBlockedMoveThresholdHaliteRatio { get; set; } = 0.15d;
        public int HarvesterMinimumFillDefault { get; set; } = 950;
        public int HarvesterMinimumFillWhenBlockedByOpponent { get; set; } = 700;
        public int HarvesterMaximumFillForTurningOutbound { get; set; } = 650;
        public double HarvesterToOutboundConversionMinJobTimeRatio { get; set; } = 1.5d;
        public double HarvesterAllowedOverfillRatio { get; set; } = 0.7d;

        public int FugitiveShipConversionMinBlockedTurnCount { get; set; } = 60;
        public double FugitiveShipConversionRatio { get; set; } = 0.5d;
        public int FugitiveShipMinTurnCount { get; set; } = 2;
        public int FugitiveShipMaxTurnCount { get; set; } = 4;

        public double EarlyGameTargetShipRatio { get; set; } = 0.8d;
        public bool IsEarlyGameFeatureEnabled { get; set; } = true;
        public int EarlyGameShipMinReturnedHalite { get { return GetEarlyGameShipMinReturnedHalite(); } }
        public bool IsTwoPlayerAggressiveModeEnabled { get; set; } = false;
        public int DetourTurnCount { get; set; } = 5;
        public double LemmingDropoffTurnCapacity { get; set; } = 3d;
        public double SimulatorAssumedSunkShipRatio { get; set; } = 0.02d;
        public int ShipSurroundRadius { get; set; } = 5;

        public int OpponentHarvestAreaMapMaxScentStrength { get; set; } = 20;
        public int OpponentHarvestAreaMapHaliteBonusExtraRadius { get; set; } = 0;

        public double SimulatorHarvestRatioMultiplier { get; set; } = 1d;

        private int earlyGameShipMinReturnedHalite = -1;

        private int GetEarlyGameShipMinReturnedHalite()
        {
            if (earlyGameShipMinReturnedHalite == -1)
            {
                int targetShipCount = (int)Math.Round((GameConstants.InitialHalite / (double)GameConstants.ShipCost) * EarlyGameTargetShipRatio);
                int targetShipCountCost = targetShipCount * GameConstants.ShipCost;
                int earlyGameShipCount = GameConstants.InitialHalite / GameConstants.ShipCost;
                earlyGameShipMinReturnedHalite = (int)Math.Ceiling(targetShipCountCost / (double)earlyGameShipCount);
            }

            return earlyGameShipMinReturnedHalite;
        }
    }
}
