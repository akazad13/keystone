using System.Net;
using System.Text;

namespace Keystone.Bff.Tests.Fakes;

/// <summary>An HTTP handler that answers every request with a canned response and records what was sent.</summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly List<IReadOnlyDictionary<string, string>> _forms = [];

    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

    public string ResponseBody { get; set; } = "{}";

    /// <summary>Delay before answering, to let concurrent callers pile up.</summary>
    public TimeSpan Delay { get; set; }

    public int RequestCount
    {
        get { lock (_gate) { return _forms.Count; } }
    }

    /// <summary>The form fields of every request, in order.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Forms
    {
        get { lock (_gate) { return [.. _forms]; } }
    }

    public List<Uri?> RequestUris { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var form = body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : string.Empty);

        lock (_gate)
        {
            _forms.Add(form);
            RequestUris.Add(request.RequestUri);
        }

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        return new HttpResponseMessage(StatusCode)
        {
            Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
        };
    }
}
