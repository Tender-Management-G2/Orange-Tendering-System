namespace Orange_Tendering_System.Services.Radar;

// ORANGE RADAR analytics configuration (appsettings section "OrangeRadar"): detector thresholds, the demo dataset switch and
// the snapshot cache. The AI Studio's Gemini settings live in the separate "Gemini" section (IntelligenceAiService).
public sealed class RadarOptions
{
    public RadarThresholds Thresholds { get; set; } = new();
    public RadarDemoOptions DemoDataset { get; set; } = new();
    // Analytics snapshot cache per user and period. Short, because officers expect new bids to appear quickly.
    public int CacheSeconds { get; set; } = 60;
}

// Conservative, configurable demonstration thresholds for the forensic detectors (docs/ORANGE_RADAR.md explains each one).
public sealed class RadarThresholds
{
    public int MinimumBidsForCompetition { get; set; } = 3;
    public int JointParticipationMinRounds { get; set; } = 3;
    // Network "competition" view: suppliers are linked when they both submitted offers in at least this many distinct closed rounds.
    public int CompetitionLinkMinRounds { get; set; } = 2;
    public int PriceGapMinRounds { get; set; } = 3;
    public double PriceGapMaxVariation { get; set; } = 0.02;
    public double PriceGapMinRatio { get; set; } = 1.005;
    public double PriceGapMaxRatio { get; set; } = 1.15;
    public int TimingMinRounds { get; set; } = 3;
    public int TimingWindowMinutes { get; set; } = 10;
}

public sealed class RadarDemoOptions
{
    // Seeds the clearly labeled fictional "RADAR DEMO" dataset. Honoured only in Development and only for a database whose
    // name ends with "_RadarDemo", so the shared team database can never receive these fixtures.
    public bool Enabled { get; set; }
}
