using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Options;
using WindowsAgent.Configuration;

namespace WindowsAgent.Voice;

/// <summary>
/// Speech-to-text via Azure Cognitive Services. Azure Speech has a free (F0)
/// tier with a monthly quota — fine for a personal assistant, just watch usage
/// if it gets used heavily.
/// </summary>
public class AzureSpeechService
{
    private readonly AzureSpeechOptions _options;

    public AzureSpeechService(IOptions<AzureSpeechOptions> options) => _options = options.Value;

    /// <summary>Transcribes a single WAV audio buffer (16kHz mono PCM recommended) to text.</summary>
    public async Task<string> TranscribeAsync(Stream wavAudio, CancellationToken ct)
    {
        var speechConfig = SpeechConfig.FromSubscription(_options.SubscriptionKey, _options.Region);
        speechConfig.SpeechRecognitionLanguage = "en-US";

        using var audioInput = AudioConfig.FromStreamInput(new PullAudioInputStreamFromStream(wavAudio));
        using var recognizer = new SpeechRecognizer(speechConfig, audioInput);

        var result = await recognizer.RecognizeOnceAsync().WaitAsync(ct);

        return result.Reason == ResultReason.RecognizedSpeech
            ? result.Text
            : throw new InvalidOperationException($"Speech not recognized: {result.Reason}");
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
