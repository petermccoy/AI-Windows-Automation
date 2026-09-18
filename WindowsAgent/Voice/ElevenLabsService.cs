using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using WindowsAgent.Configuration;

namespace WindowsAgent.Voice;

/// <summary>Text-to-speech via ElevenLabs. Returns raw MP3 bytes for the
/// Blazor page to play through an &lt;audio&gt; element.</summary>
public class ElevenLabsService
{
    private readonly HttpClient _http;
    private readonly ElevenLabsOptions _options;

    public ElevenLabsService(HttpClient http, IOptions<ElevenLabsOptions> options)
    {
        _options = options.Value;
        http.BaseAddress = new Uri("https://api.elevenlabs.io/");
        http.DefaultRequestHeaders.Add("xi-api-key", _options.ApiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));
        _http = http;
    }

    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(
            $"v1/text-to-speech/{_options.VoiceId}",
            new { text, model_id = "eleven_turbo_v2_5" },
            ct);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }
}
