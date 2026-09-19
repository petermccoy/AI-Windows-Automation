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
    private readonly AppSettingsStore _settings;

    public AzureSpeechService(AppSettingsStore settings) => _settings = settings;

    /// <summary>Transcribes a single WAV audio buffer (16kHz mono PCM recommended) to text.</summary>
    public async Task<string> TranscribeAsync(Stream wavAudio, CancellationToken ct)
    {
        var settings = _settings.Current.AzureSpeech;
        var speechConfig = SpeechConfig.FromSubscription(settings.SubscriptionKey, settings.Region);
        speechConfig.SpeechRecognitionLanguage = "en-US";

        using var audioInput = AudioConfig.FromStreamInput(new PullAudioInputStreamFromStream(wavAudio));
        using var recognizer = new SpeechRecognizer(speechConfig, audioInput);

        var result = await recognizer.RecognizeOnceAsync().WaitAsync(ct);

        return result.Reason == ResultReason.RecognizedSpeech
            ? result.Text
            : throw new InvalidOperationException($"Speech not recognized: {result.Reason}");
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
        using var result = await synthesizer.SpeakTextAsync(text).WaitAsync(ct);

        return result.Reason == ResultReason.SynthesizingAudioCompleted
            ? result.AudioData
            : throw new InvalidOperationException($"Speech synthesis failed: {result.Reason}");
    }
}

/// <summary>Adapts a plain Stream to the PullAudioInputStream the Speech SDK expects.</summary>
internal class PullAudioInputStreamFromStream : PullAudioInputStreamCallback
{
    private readonly Stream _stream;
    public PullAudioInputStreamFromStream(Stream stream) => _stream = stream;

    public override int Read(byte[] dataBuffer, uint size) =>
        _stream.Read(dataBuffer, 0, (int)size);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _stream.Dispose();
        base.Dispose(disposing);
    }
}
