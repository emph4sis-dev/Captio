using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Captio.Core.Imgur;

public enum UploadFailureKind
{
    Network,
    Timeout,
    RateLimited,
    Rejected,
    ServerError,
    Cancelled,
}

public abstract record UploadResult;

public sealed record UploadSucceeded(Uri Link) : UploadResult;

/// <param name="RetryAfter">Wait suggested by the service, when it reports one.</param>
public sealed record UploadFailed(UploadFailureKind Kind, TimeSpan? RetryAfter = null) : UploadResult;

/// <summary>
/// Anonymous image upload using only a public Client-ID. Never logs or keeps the
/// returned link, id or deletion token; the caller owns the link until it closes the result.
/// </summary>
public sealed class ImgurUploader : IDisposable
{
    public static readonly Uri Endpoint = new("https://api.imgur.com/3/image");

    private readonly HttpClient _http;

    public ImgurUploader(string clientId, HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(clientId)) throw new ArgumentException("A Client-ID is required.", nameof(clientId));

        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = timeout ?? TimeSpan.FromSeconds(60);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Client-ID", clientId);
    }

    public async Task<UploadResult> UploadAsync(ReadOnlyMemory<byte> png, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent
        {
            { new ReadOnlyMemoryContent(png) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } }, "image", "capture.png" },
            { new StringContent("file"), "type" },
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(Endpoint, content, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new UploadFailed(UploadFailureKind.Cancelled);
        }
        catch (OperationCanceledException)
        {
            return new UploadFailed(UploadFailureKind.Timeout);
        }
        catch (HttpRequestException)
        {
            return new UploadFailed(UploadFailureKind.Network);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new UploadFailed(UploadFailureKind.RateLimited, ReadRetryAfter(response));
            if ((int)response.StatusCode >= 500)
                return new UploadFailed(UploadFailureKind.ServerError, ReadRetryAfter(response));
            if (!response.IsSuccessStatusCode)
                return new UploadFailed(UploadFailureKind.Rejected);

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                // Only the public link is read; the deletion token is never extracted.
                if (json.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("link", out var link) &&
                    Uri.TryCreate(link.GetString(), UriKind.Absolute, out var uri) &&
                    uri.Scheme == Uri.UriSchemeHttps)
                {
                    return new UploadSucceeded(uri);
                }
                return new UploadFailed(UploadFailureKind.Rejected);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new UploadFailed(UploadFailureKind.Cancelled);
            }
            catch (JsonException)
            {
                return new UploadFailed(UploadFailureKind.Rejected);
            }
        }
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is { } retry)
        {
            if (retry.Delta is { } delta) return delta;
            if (retry.Date is { } date) return Positive(date - DateTimeOffset.UtcNow);
        }
        if (response.Headers.TryGetValues("X-RateLimit-UserReset", out var values) &&
            long.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
        {
            return Positive(DateTimeOffset.FromUnixTimeSeconds(epoch) - DateTimeOffset.UtcNow);
        }
        return null;
    }

    private static TimeSpan? Positive(TimeSpan span) => span > TimeSpan.Zero ? span : null;

    public void Dispose() => _http.Dispose();
}
