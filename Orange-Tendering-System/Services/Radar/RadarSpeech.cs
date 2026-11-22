using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Whisper.net;

namespace Orange_Tendering_System.Services.Radar;

// Confidence is the mean probability of the recognised text tokens (0-1). Reliable transcripts may be sent automatically; the
// page asks the user to confirm or correct the others. LanguageProbability is Whisper's own guess (1 when the user chose it).
public sealed record RadarTranscript(string Text, string Language, double AudioSeconds, double ProcessingMs,
    double Confidence = 0, bool Reliable = false, double LanguageProbability = 1, bool LanguageArbitrated = false);
public sealed class RadarSpeechException(string message, int status = 503) : Exception(message) { public int Status { get; } = status; }

// Local speech recognition with Whisper.net (Vulkan GPU, CPU fallback). The model is loaded once and kept; each utterance is a
// 16 kHz mono WAV cut by the page's endpointer (wwwroot/js/orange-radar-audio.js). Audio is processed in memory and never stored.
// Transcription only: translation is never enabled, so speech stays in the language it was spoken in.
public sealed class RadarSpeechRecognizer(IOptions<RadarOptions> options, ILogger<RadarSpeechRecognizer> logger) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private WhisperFactory? factory;
    private string? loadError;

    public string? ModelPath => RadarPaths.Expand(options.Value.Whisper.ModelPath);
    public bool Configured => File.Exists(ModelPath);
    public string State => !Configured ? "model not found" : loadError ?? (factory == null ? "ready (loads on first use)" : $"loaded ({Whisper.net.LibraryLoader.RuntimeOptions.LoadedLibrary})");

    // "auto" tries the GPU through Vulkan (no CUDA toolkit needed) and falls back to the CPU; "cpu" forces the CPU.
    private static readonly object RuntimeLock = new();
    private static bool runtimeChosen;
    private void ChooseRuntime()
    {
        lock (RuntimeLock)
        {
            if (runtimeChosen) return;
            Whisper.net.LibraryLoader.RuntimeOptions.RuntimeLibraryOrder = options.Value.Whisper.Runtime?.ToLowerInvariant() == "cpu"
                ? [Whisper.net.LibraryLoader.RuntimeLibrary.Cpu]
                : [Whisper.net.LibraryLoader.RuntimeLibrary.Vulkan, Whisper.net.LibraryLoader.RuntimeLibrary.Cpu];
            runtimeChosen = true;
        }
    }

    public async Task<RadarTranscript> TranscribeAsync(Stream wav, string language, CancellationToken ct)
    {
        if (!Configured) throw new RadarSpeechException("Whisper model not found. Set OrangeRadar:Whisper:ModelPath (see docs/ORANGE_RADAR.md). Text input still works.");
        var samples = ReadWav(wav, options.Value.Whisper.MaxSeconds);
        if (samples.Length < 16000 * 0.3) throw new RadarSpeechException("The recording was too short.", 400);
        if (!await gate.WaitAsync(TimeSpan.FromSeconds(20), ct)) throw new RadarSpeechException("Speech recognition is busy. Try again.", 429);
        var watch = Stopwatch.StartNew();
        try
        {
            try { ChooseRuntime(); factory ??= WhisperFactory.FromPath(ModelPath!); }
            catch (Exception e) { loadError = "failed to load: " + e.Message; logger.LogWarning(e, "Whisper model failed to load"); throw new RadarSpeechException("The Whisper runtime could not load the model."); }
            var settings = options.Value.Whisper;
            double languageProbability = 1; var arbitrated = false;
            Decoded best;
            if (language is "ar" or "en") best = await DecodeAsync(samples, language, ct);
            else if (settings.LanguageConfidence <= 0) best = await DecodeAsync(samples, "auto", ct);
            else
            {
                // Whisper's guess covers ~100 languages and is unstable on short or accented commands (English heard as Arabic,
                // Persian or Urdu). Only Arabic and English are supported here, so an unsure guess is settled by decoding both.
                await using (var detector = Builder(samples.Length, "auto").Build())
                {
                    var (code, probability) = detector.DetectLanguageWithProbability(samples);
                    languageProbability = probability;
                    language = code ?? "";
                }
                if (language is "ar" or "en" && languageProbability >= settings.LanguageConfidence) best = await DecodeAsync(samples, language, ct);
                else
                {
                    arbitrated = true;
                    var english = await DecodeAsync(samples, "en", ct); var arabic = await DecodeAsync(samples, "ar", ct);
                    best = arabic.Confidence > english.Confidence ? arabic : english;
                }
            }
            var text = Clean(best.Text);
            var detected = best.Language is "ar" or "en" ? best.Language : RadarGrounding.IsArabic(text) ? "ar" : "en";
            var reliable = text.Length > 0 && best.Confidence >= settings.ReliableConfidence && !EchoesPrompt(text, detected);
            if (!reliable && EchoesPrompt(text, detected)) text = "";
            return new RadarTranscript(text, detected, samples.Length / 16000.0, watch.Elapsed.TotalMilliseconds, Math.Round(best.Confidence, 3), reliable,
                Math.Round(languageProbability, 3), arbitrated);
        }
        finally { gate.Release(); }
    }

    private sealed record Decoded(string Text, string Language, double Confidence);

    private WhisperProcessorBuilder Builder(int sampleCount, string language)
    {
        var settings = options.Value.Whisper;
        var builder = factory!.CreateBuilder()
            .WithThreads(Math.Clamp(settings.Threads, 1, Environment.ProcessorCount))
            .WithNoContext().WithSingleSegment().WithNoSpeechThreshold(0.6f).WithProbabilities()
            .WithLanguage(language);
        // Whisper always encodes a 30 s window (1500 frames, 50 per second); the adaptive option sizes it to the utterance.
        if (settings.AdaptiveAudioContext)
            builder = builder.WithAudioContextSize(Math.Min(1500, (int)Math.Ceiling((sampleCount / 16000.0 + 2) * 50 / 64.0) * 64));
        if (settings.BeamSize > 1) builder = builder.WithBeamSearchSamplingStrategy(beam => beam.WithBeamSize(Math.Min(8, settings.BeamSize)));
        return builder;
    }

    private string? Prompt(string language) => language switch
    {
        "en" => options.Value.Whisper.EnglishPrompt,
        "ar" => options.Value.Whisper.ArabicPrompt,
        _ => null
    };

    private async Task<Decoded> DecodeAsync(float[] samples, string language, CancellationToken ct)
    {
        var builder = Builder(samples.Length, language);
        if (Prompt(language) is { Length: > 0 } prompt) builder = builder.WithPrompt(prompt);
        await using var processor = builder.Build();
        var text = new StringBuilder(); var detected = language; double sum = 0; var count = 0;
        await foreach (var segment in processor.ProcessAsync(samples, ct))
        {
            text.Append(segment.Text);
            if (!string.IsNullOrEmpty(segment.Language)) detected = segment.Language;
            // Text tokens only: timestamps and control tokens ([_BEG_], <|en|>) say nothing about recognition quality.
            foreach (var token in segment.Tokens ?? [])
                if (token.Text is { Length: > 0 } t && !t.StartsWith("[_") && !t.StartsWith("<|")) { sum += token.Probability; count++; }
            if (count == 0 && segment.Probability > 0) { sum += segment.Probability; count++; }
        }
        return new Decoded(text.ToString(), detected, count == 0 ? 0 : sum / count);
    }

    // With nothing intelligible to transcribe, Whisper can repeat its prompt; that is never the user's request.
    private bool EchoesPrompt(string text, string language)
    {
        var prompt = Prompt(language);
        if (string.IsNullOrWhiteSpace(prompt) || text.Length < 12) return false;
        static string Norm(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var t = Norm(text);
        return t.Length >= 10 && Norm(prompt).Contains(t[..Math.Min(t.Length, 40)]);
    }

    // Loads the model (and the GPU runtime) when a conversation starts, so the first spoken turn is not a cold start.
    public async Task<double> WarmAsync(CancellationToken ct)
    {
        if (!Configured) return 0;
        var silence = new MemoryStream();
        using (var w = new BinaryWriter(silence, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            var samples = 16000; w.Write("RIFF"u8); w.Write(36 + samples * 2); w.Write("WAVE"u8); w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(16000); w.Write(32000); w.Write((short)2); w.Write((short)16); w.Write("data"u8); w.Write(samples * 2); w.Write(new byte[samples * 2]);
        }
        silence.Position = 0;
        try { return (await TranscribeAsync(silence, "en", ct)).ProcessingMs; }
        catch (RadarSpeechException) { return 0; }
    }

    // Whisper's well-known outputs for non-speech (music tags, subtitle credits) are dropped instead of being sent to the analyst.
    private static string Clean(string text)
    {
        var t = System.Text.RegularExpressions.Regex.Replace(text, @"\[[^\]]*\]|\([^)]*(music|موسيقى)[^)]*\)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        string[] noise = ["Thank you.", "Thanks for watching!", "you", "اشتركوا في القناة", "ترجمة نانسي قنقر"];
        return noise.Any(n => string.Equals(t, n, StringComparison.OrdinalIgnoreCase)) ? "" : t;
    }

    // 16-bit PCM mono 16 kHz WAV (produced by the page's capture pipeline) to float samples.
    public static float[] ReadWav(Stream stream, int maxSeconds)
    {
        using var ms = new MemoryStream(); stream.CopyTo(ms);
        var data = ms.GetBuffer().AsSpan(0, (int)ms.Length);
        if (data.Length < 44 || !data[..4].SequenceEqual("RIFF"u8) || !data[8..12].SequenceEqual("WAVE"u8)) throw new RadarSpeechException("Audio must be a WAV file.", 400);
        var pos = 12; int channels = 0, rate = 0, bits = 0;
        while (pos + 8 <= data.Length)
        {
            var id = Encoding.ASCII.GetString(data.Slice(pos, 4)); var size = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos + 4, 4));
            if (size < 0 || pos + 8 + size > data.Length && id != "data") break;
            if (id == "fmt ")
            {
                channels = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(pos + 10, 2)); rate = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos + 12, 4)); bits = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(pos + 22, 2));
            }
            else if (id == "data")
            {
                if (channels != 1 || rate != 16000 || bits != 16) throw new RadarSpeechException("Audio must be 16 kHz mono 16-bit PCM.", 400);
                var length = Math.Min(Math.Min(size, data.Length - pos - 8), maxSeconds * 16000 * 2);
                var samples = new float[length / 2];
                for (var i = 0; i < samples.Length; i++) samples[i] = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(pos + 8 + i * 2, 2)) / 32768f;
                return samples;
            }
            pos += 8 + size + (size & 1);
        }
        throw new RadarSpeechException("The WAV file has no audio data.", 400);
    }

    public void Dispose() { factory?.Dispose(); gate.Dispose(); }
}

// Local neural speech with Piper on the CPU (keeps the GPU for the language model). One persistent Python worker holds both voices
// in memory, so a sentence costs only synthesis time; it talks over stdin/stdout (no network port) and is stopped with the app.
public sealed class RadarSpeechSynthesizer(IOptions<RadarOptions> options, IWebHostEnvironment environment, ILogger<RadarSpeechSynthesizer> logger) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Process? worker;
    private string? lastError;

    private RadarPiperOptions Settings => options.Value.Piper;
    private string? Python => RadarPaths.Expand(Settings.PythonExecutable);
    private string? Voices => RadarPaths.Expand(Settings.VoicesDirectory);
    private string Script => Path.IsPathRooted(Settings.WorkerScript) ? Settings.WorkerScript : Path.Combine(environment.ContentRootPath, Settings.WorkerScript);
    public string VoicePath(string language) => Path.Combine(Voices ?? "", language == "ar" ? Settings.ArabicVoice : Settings.EnglishVoice);

    public IReadOnlyDictionary<string, string> Components => new Dictionary<string, string>
    {
        ["python"] = File.Exists(Python) ? "ready" : "not found",
        ["worker"] = File.Exists(Script) ? (worker is { HasExited: false } ? "running" : lastError ?? "starts on first use") : "script missing",
        ["arabicVoice"] = File.Exists(VoicePath("ar")) && File.Exists(VoicePath("ar") + ".json") ? "ready" : "not found",
        ["englishVoice"] = File.Exists(VoicePath("en")) && File.Exists(VoicePath("en") + ".json") ? "ready" : "not found"
    };
    public bool Configured => Components.Values.All(v => v is "ready" or "running" or "starts on first use") ;

    public async Task<(byte[] Wav, double Ms)> SynthesizeAsync(string text, string language, double speed, CancellationToken ct)
    {
        text = RadarGrounding.Speakable(text, language == "ar");
        if (text.Length == 0) throw new RadarSpeechException("Nothing to say.", 400);
        if (text.Length > Settings.MaxCharacters) text = text[..Settings.MaxCharacters];
        if (!Configured) throw new RadarSpeechException("Piper speech is not configured (python, worker script or voice files missing). Answers stay visible as text.");
        if (!await gate.WaitAsync(TimeSpan.FromSeconds(30), ct)) throw new RadarSpeechException("Speech synthesis is busy.", 429);
        var watch = Stopwatch.StartNew();
        try
        {
            var process = await EnsureWorkerAsync(ct);
            var request = JsonSerializer.Serialize(new { text, voice = language == "ar" ? "ar" : "en", length_scale = Math.Round(1 / Math.Clamp(speed, 0.6, 1.6), 3) });
            ct.ThrowIfCancellationRequested();
            await process.StandardInput.WriteLineAsync(request);
            await process.StandardInput.FlushAsync();
            var stdout = process.StandardOutput.BaseStream;
            var header = new byte[5];
            // Once a sentence is sent, its frame is always read (one sentence takes well under a second), even if the browser
            // gave up meanwhile - otherwise the stream would desynchronize and the voices would have to be reloaded.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await stdout.ReadExactlyAsync(header, timeout.Token);
            var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1));
            if (length is < 0 or > 20_000_000) { Kill(); throw new RadarSpeechException("The speech worker returned an invalid frame."); }
            var payload = new byte[length];
            await stdout.ReadExactlyAsync(payload, timeout.Token);
            if (header[0] != (byte)'W') throw new RadarSpeechException("Speech synthesis failed: " + Encoding.UTF8.GetString(payload));
            return (payload, watch.Elapsed.TotalMilliseconds);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or EndOfStreamException)
        {
            // A half-read frame would desynchronize the protocol, so the worker is always replaced after an interrupted read.
            Kill();
            lastError = "restarting after error";
            throw new RadarSpeechException("The speech worker stopped unexpectedly; it will restart on the next sentence.");
        }
        finally { gate.Release(); }
    }

    private async Task<Process> EnsureWorkerAsync(CancellationToken ct)
    {
        if (worker is { HasExited: false }) return worker;
        var start = new ProcessStartInfo(Python!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardErrorEncoding = Encoding.UTF8 };
        // -I: isolated mode, so nothing from the working directory or PYTHON* variables is imported.
        foreach (var arg in new[] { "-I", Script, "--ar", VoicePath("ar"), "--en", VoicePath("en") }) start.ArgumentList.Add(arg);
        var process = Process.Start(start) ?? throw new RadarSpeechException("The Piper worker could not start.");
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            if (e.Data.StartsWith("READY")) ready.TrySetResult(true);
            else logger.LogDebug("Piper worker: {Line}", e.Data);
        };
        process.BeginErrorReadLine();
        var finished = await Task.WhenAny(ready.Task, process.WaitForExitAsync(ct), Task.Delay(TimeSpan.FromSeconds(60), ct));
        if (finished != ready.Task) { try { process.Kill(true); } catch (InvalidOperationException) { } lastError = "failed to start"; throw new RadarSpeechException("The Piper worker did not start. Check python and piper-tts (docs/ORANGE_RADAR.md)."); }
        lastError = null; worker = process;
        logger.LogInformation("Orange Radar Piper worker started (pid {Pid})", process.Id);
        return process;
    }

    private void Kill() { try { if (worker is { HasExited: false }) worker.Kill(true); } catch (InvalidOperationException) { } worker?.Dispose(); worker = null; }
    public void Dispose() { Kill(); gate.Dispose(); }
}
