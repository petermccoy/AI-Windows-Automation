using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using WindowsAgent.Configuration;

namespace WindowsAgent.Voice;

/// <summary>
/// Speech-to-text and text-to-speech via Azure Cognitive Services — native on
/// both ends, so no separate TTS provider (e.g. ElevenLabs) is required. Azure
/// Speech has a free (F0) tier with a monthly quota — fine for a personal
/// assistant, just watch usage if it gets used heavily. Reads credentials from
/// AppSettingsStore per call so a Settings-page edit applies immediately.
/// </summary>
public class AzureSpeechService
{
    // Azure Speech normally responds in well under a second; a healthy call that's
    // still running after this long means the network path to the Speech endpoint
    // is stalling (proxy/firewall/VPN), not that recognition is "still thinking."
    // Without a bound here, a stalled call can block the calling Blazor circuit's
    // synchronization context long enough for the client's SignalR keep-alive to
    // give up and force a reconnect — this turns that into a fast, clear failure.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly AppSettingsStore _settings;

    public AzureSpeechService(AppSettingsStore settings) => _settings = settings;

    /// <summary>Transcribes a canonical-header WAV byte array to text. Returns ""
    /// (not an exception) if the audio was silence or unrecognizable — agent.js's
    /// Web Audio API recorder produces real PCM WAV here, not a MediaRecorder blob
    /// mislabeled as one, so the header this parses is trustworthy.</summary>
    public async Task<string> TranscribeAsync(byte[] wavBytes, CancellationToken ct)
    {
        var settings = _settings.Current.AzureSpeech;

        var (sampleRate, bitsPerSample, channels, dataOffset) = ParseWavHeader(wavBytes);

        var format = AudioStreamFormat.GetWaveFormatPCM((uint)sampleRate, (byte)bitsPerSample, (byte)channels);
        using var pushStream = AudioInputStream.CreatePushStream(format);
        pushStream.Write(wavBytes[dataOffset..]);
        pushStream.Close();

        var speechConfig = SpeechConfig.FromSubscription(settings.SubscriptionKey, settings.Region);
        speechConfig.SpeechRecognitionLanguage = "en-US";

        using var audioConfig = AudioConfig.FromStreamInput(pushStream);
        using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);

        SpeechRecognitionResult result;
        try
        {
            result = await recognizer.RecognizeOnceAsync().WaitAsync(RequestTimeout, ct);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException(
                $"Azure Speech didn't respond within {RequestTimeout.TotalSeconds:0}s — check " +
                "AzureSpeech:SubscriptionKey/Region on the Settings page and network connectivity " +
                "to Azure (proxy/firewall/VPN can silently block this).");
        }

        return result.Reason switch
        {
            ResultReason.RecognizedSpeech => result.Text,
            ResultReason.NoMatch => "", // silence or nothing understood — treat as empty, not an error
            ResultReason.Canceled => throw new InvalidOperationException(
                $"Speech recognition canceled: {CancellationDetails.FromResult(result).ErrorDetails}"),
            _ => throw new InvalidOperationException($"Unexpected recognition result: {result.Reason}")
        };
    }

    /// <summary>Minimal RIFF/WAVE header parser. Scans for the "fmt " and "data"
    /// chunks rather than assuming a fixed 44-byte layout, since some encoders
    /// insert extra chunks (e.g. LIST/INFO) before the data chunk.</summary>
    private static (int sampleRate, int bitsPerSample, int channels, int dataOffset) ParseWavHeader(byte[] wav)
    {
        if (wav.Length < 44 || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F')
            throw new ArgumentException("Not a valid WAV file (missing RIFF header).");

        int channels = 1, sampleRate = 16000, bitsPerSample = 16;
        int pos = 12; // after "RIFF"<size>"WAVE"

        while (pos + 8 <= wav.Length)
        {
            var chunkId = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
            var chunkSize = BitConverter.ToInt32(wav, pos + 4);
            var chunkDataStart = pos + 8;

            if (chunkId == "fmt ")
            {
                channels = BitConverter.ToInt16(wav, chunkDataStart + 2);
                sampleRate = BitConverter.ToInt32(wav, chunkDataStart + 4);
                bitsPerSample = BitConverter.ToInt16(wav, chunkDataStart + 14);
            }
            else if (chunkId == "data")
            {
                return (sampleRate, bitsPerSample, channels, chunkDataStart);
            }

            pos = chunkDataStart + chunkSize + (chunkSize % 2); // chunks are word-aligned
        }

        throw new ArgumentException("WAV file has no 'data' chunk.");
    }

    /// <summary>Synthesizes speech to MP3 bytes for playback through the browser's
    /// &lt;audio&gt; element (same wire format the ElevenLabs path used).</summary>
    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken ct)
    {
        var settings = _settings.Current.AzureSpeech;
        var speechConfig = SpeechConfig.FromSubscription(settings.SubscriptionKey, settings.Region);
        speechConfig.SpeechSynthesisVoiceName = settings.Voice;
        speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Audio16Khz32KBitRateMonoMp3);

        // Passing a null AudioConfig means "don't play to a local device" — the
        // synthesized audio comes back in result.AudioData instead. The cast
        // disambiguates from the SpeechSynthesizer(SpeechConfig, AutoDetectSourceLanguageConfig)
        // overload, which a bare null would otherwise be ambiguous against.
        using var synthesizer = new SpeechSynthesizer(speechConfig, (AudioConfig?)null);

        SpeechSynthesisResult? result = null;
        try
        {
            result = await synthesizer.SpeakTextAsync(text).WaitAsync(RequestTimeout, ct);

            return result.Reason == ResultReason.SynthesizingAudioCompleted
                ? result.AudioData
                : throw new InvalidOperationException($"Speech synthesis failed: {result.Reason}");
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException(
                $"Azure Speech didn't respond within {RequestTimeout.TotalSeconds:0}s — check " +
                "AzureSpeech:SubscriptionKey/Region on the Settings page and network connectivity " +
                "to Azure (proxy/firewall/VPN can silently block this).");
        }
        finally
        {
            result?.Dispose();
        }
    }
}
