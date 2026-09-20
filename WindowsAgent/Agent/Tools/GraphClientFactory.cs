using Azure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using WindowsAgent.Configuration;

namespace WindowsAgent.Agent.Tools;

/// <summary>
/// Builds (and caches) the app-only Microsoft Graph client shared by every
/// Graph-backed tool (send_email, read_calendar, read_email), so the
/// ClientSecretCredential/GraphServiceClient setup lives in one place instead
/// of being copy-pasted per tool.
/// </summary>
public class GraphClientFactory
{
    private readonly GraphOptions _options;
    private GraphServiceClient? _client;

    public GraphClientFactory(IOptions<GraphOptions> options) => _options = options.Value;

    public string SenderUserPrincipalName => _options.SenderUserPrincipalName;

    public GraphServiceClient GetClient()
    {
        if (_client != null) return _client;

        var credential = new ClientSecretCredential(
            _options.TenantId, _options.ClientId, _options.ClientSecret);

        _client = new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });
        return _client;
    }
}
