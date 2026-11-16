using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Orange_Tendering_System.Services;

public sealed class ExecutiveSpeechOptions
{
    public string? WhisperExecutable { get; set; }
    public string? ModelPath { get; set; }
    public string? FfmpegExecutable { get; set; }
}

// Optional self-hosted whisper.cpp adapter. Paths are administrator configuration only.
// It never downloads a model, invokes a shell, accepts client paths, or calls a remote STT service.
public sealed class ExecutiveSpeechService(IOptions<ExecutiveSpeechOptions> options)
{
    private static readonly SemaphoreSlim RecognitionGate = new(1, 1);
    public bool Available => File.Exists(options.Value.WhisperExecutable) && File.Exists(options.Value.ModelPath) && File.Exists(options.Value.FfmpegExecutable);
    /// <summary>Per-component state for administrators: "ready", "not configured" or "file not found". Paths are never returned.</summary>
    public IReadOnlyDictionary<string, string> Components => new Dictionary<string, string>
    {
        ["whisper"] = State(options.Value.WhisperExecutable), ["model"] = State(options.Value.ModelPath), ["ffmpeg"] = State(options.Value.FfmpegExecutable)
    };
    private static string State(string? path) => string.IsNullOrWhiteSpace(path) ? "not configured" : File.Exists(path) ? "ready" : "file not found";
    public async Task<string> Transcribe(IFormFile audio, string language, CancellationToken ct)
    {
        if (!Available) throw new AgentException("Local Whisper runtime/model is not configured. Use text or an installed browser language pack.", 503);
        if (language is not ("ar" or "en") || audio.Length is <= 0 or > 5_000_000) throw new AgentException("Record up to 30 seconds of Arabic or English audio (maximum 5 MB).");
        if (!await RecognitionGate.WaitAsync(0, ct)) throw new AgentException("Local recognition is busy. Try again shortly or type your command.", 429);
        var folder = Path.Combine(Path.GetTempPath(), "OrangeAgentSpeech_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var input = Path.Combine(folder, "recording.webm"); var wav = Path.Combine(folder, "recording.wav"); var output = Path.Combine(folder, "transcript");
            await using (var file = File.Create(input)) await audio.CopyToAsync(file, ct);
            await Run(options.Value.FfmpegExecutable!, ["-nostdin", "-hide_banner", "-loglevel", "error", "-i", input, "-t", "30", "-ar", "16000", "-ac", "1", "-y", wav], ct);
            await Run(options.Value.WhisperExecutable!, ["-m", options.Value.ModelPath!, "-f", wav, "-l", language, "-otxt", "-of", output, "-np"], ct);
            var text = (await File.ReadAllTextAsync(output + ".txt", ct)).Trim();
            if (text.Length == 0 || text.Length > 2000) throw new AgentException("No usable transcript was produced. Try a shorter recording or type the command.");
            return text;
        }
        finally
        {
            try
            {
                var expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
                var actual = Path.GetFullPath(folder);
                if (!string.Equals(Path.GetDirectoryName(actual), expectedParent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(actual).StartsWith("OrangeAgentSpeech_", StringComparison.Ordinal))
                    throw new IOException("Unexpected speech cleanup path.");
                Directory.Delete(actual, true);
            }
            finally { RecognitionGate.Release(); }
        }
    }
    private static async Task Run(string executable, string[] arguments, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new AgentException("Local speech process could not start.", 503);
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token); var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); await Task.WhenAll(stdout, stderr); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw new AgentException("Local transcription timed out. Type the command or try a shorter recording.", 503); }
        if (process.ExitCode != 0) throw new AgentException("Local speech recognition failed. Check the installed runtime/model and audio format.", 503);
    }
}
