namespace Orange_Tendering_System.Services.Radar;

// ORANGE RADAR configuration (appsettings section "OrangeRadar"). Every path and model name is configuration so team members
// with different hardware or folders only change appsettings / User Secrets. Nothing here is required for the analytics:
// without Ollama, Whisper or Piper the market and network views keep working.
public sealed class RadarOptions
{
    public RadarModelOptions Models { get; set; } = new();
    public RadarThresholds Thresholds { get; set; } = new();
    public RadarWhisperOptions Whisper { get; set; } = new();
    public RadarPiperOptions Piper { get; set; } = new();
    public RadarDemoOptions DemoDataset { get; set; } = new();
    // Analytics snapshot cache per user and period. Short, because officers expect new bids to appear quickly.
    public int CacheSeconds { get; set; } = 60;
}

public sealed class RadarModelOptions
{
    // Loopback Ollama only: procurement data never leaves this machine.
    public string BaseUrl { get; set; } = "http://127.0.0.1:11434";
    // Benchmarked live model (docs/ORANGE_RADAR.md): fastest first token with correct Arabic and reliable tool calls.
    public string ChatModel { get; set; } = "qwen3.5:4b";
    // Optional deeper model for the written report interpretation. Empty = use ChatModel (avoids model switching on 8 GB VRAM).
    public string? DeepModel { get; set; }
    public string EmbeddingModel { get; set; } = "qwen3-embedding:0.6b";
    public int ContextLength { get; set; } = 8192;
    public int MaxAnswerTokens { get; set; } = 420;
    public string KeepAlive { get; set; } = "15m";
    public int TimeoutSeconds { get; set; } = 90;
    public int MaxToolRounds { get; set; } = 4;
    // Structured-output router for requests the rule parser recognised as a file or page action but could not complete.
    // Empty model = ChatModel. Thinking stays off: RadarChecks --intent-bench measured no accuracy gain for much higher latency.
    public bool IntentRouter { get; set; } = true;
    public string? RouterModel { get; set; }
    public bool RouterThink { get; set; }
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
    public double TemplateSimilarity { get; set; } = 0.88;
    public int SemanticMinChunkCharacters { get; set; } = 120;
}

public sealed class RadarWhisperOptions
{
    // ggml multilingual model, e.g. %USERPROFILE%\AI-Models\Whisper\ggml-small.bin. Environment variables are expanded.
    public string? ModelPath { get; set; } = "%USERPROFILE%\\AI-Models\\Whisper\\ggml-small.bin";
    public int Threads { get; set; } = 6;
    // "auto" = GPU via Vulkan when available, else CPU; "cpu" = CPU only (keeps all VRAM for the language model).
    public string Runtime { get; set; } = "auto";
    // Sizes Whisper's 30 s encoder window to the utterance. Measured (RadarChecks --stt-bench): same word error rate as the full
    // window on the test set and about 1.2 s faster per utterance on this GPU, so it stays on.
    public bool AdaptiveAudioContext { get; set; } = true;
    public int MaxSeconds { get; set; } = 30;
    // 1 = greedy decoding. Beam search (5) did not lower the error rate on the test set and was slower, so greedy is the default.
    public int BeamSize { get; set; } = 1;
    // Auto mode accepts Whisper's language guess only at or above this probability; below it the utterance is decoded as both
    // Arabic and English and the more confident transcript wins. 0 = single-pass auto detection (previous behaviour).
    public double LanguageConfidence { get; set; } = 0.8;
    // Transcripts below this mean token probability wait for the user's confirmation and are never sent automatically.
    // Calibrated with --stt-bench: 0.8 held 13 of 28 wrong transcripts and 8 of 101 correct ones (0.6 held none).
    public double ReliableConfidence { get; set; } = 0.8;
    // Optional decoder context per language (never used for detection). Off by default: a domain vocabulary prompt was measured
    // to raise the word error rate (Whisper small copied prompt phrases into short commands).
    public string? EnglishPrompt { get; set; }
    public string? ArabicPrompt { get; set; }
}

public sealed class RadarPiperOptions
{
    public string? PythonExecutable { get; set; } = "%LOCALAPPDATA%\\Programs\\Python\\Python312\\python.exe";
    public string? VoicesDirectory { get; set; } = "%USERPROFILE%\\AI-Models\\Piper";
    public string ArabicVoice { get; set; } = "ar_JO-kareem-medium.onnx";
    public string EnglishVoice { get; set; } = "en_US-lessac-medium.onnx";
    // Relative to the content root unless absolute.
    public string WorkerScript { get; set; } = "Tools/OrangeRadar/radar_piper_worker.py";
    public int MaxCharacters { get; set; } = 400;
}

public sealed class RadarDemoOptions
{
    // Seeds the clearly labeled fictional "RADAR DEMO" dataset. Honoured only in Development and only for a database whose
    // name ends with "_RadarDemo", so the shared team database can never receive these fixtures.
    public bool Enabled { get; set; }
}

public static class RadarPaths
{
    public static string? Expand(string? path) => string.IsNullOrWhiteSpace(path) ? null : Environment.ExpandEnvironmentVariables(path.Trim());
}
