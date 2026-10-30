using System.Net;
using System.Text;
using Captio.Core.Imgur;

namespace Captio.Core.Tests;

public class ImgurUploaderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return respond(request, cancellationToken);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static readonly byte[] Png = [1, 2, 3];

    [Fact]
    public async Task Sends_https_post_with_client_id_only()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK,
            """{"data":{"id":"abc","link":"https://i.imgur.com/abc.png","deletehash":"secret"},"success":true}""")));
        using var uploader = new ImgurUploader("public-id", handler);

        var result = await uploader.UploadAsync(Png, CancellationToken.None);

        Assert.Equal(new Uri("https://i.imgur.com/abc.png"), Assert.IsType<UploadSucceeded>(result).Link);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(Uri.UriSchemeHttps, handler.Request.RequestUri!.Scheme);
        Assert.Equal("Client-ID public-id", handler.Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Result_never_exposes_deletion_token()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK,
            """{"data":{"link":"https://i.imgur.com/abc.png","deletehash":"secret-token"}}""")));
        using var uploader = new ImgurUploader("id", handler);

        var result = await uploader.UploadAsync(Png, CancellationToken.None);

        Assert.DoesNotContain("secret-token", result.ToString());
    }

    [Fact]
    public async Task Rate_limit_reports_retry_after_from_response()
    {
        var handler = new StubHandler((_, _) =>
        {
            var response = Json(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(7));
            return Task.FromResult(response);
        });
        using var uploader = new ImgurUploader("id", handler);

        var failed = Assert.IsType<UploadFailed>(await uploader.UploadAsync(Png, CancellationToken.None));

        Assert.Equal(UploadFailureKind.RateLimited, failed.Kind);
        Assert.Equal(TimeSpan.FromMinutes(7), failed.RetryAfter);
    }

    [Fact]
    public async Task Cancellation_is_reported_as_cancelled()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Json(HttpStatusCode.OK, "{}");
        });
        using var uploader = new ImgurUploader("id", handler);
        using var cts = new CancellationTokenSource(50);

        var failed = Assert.IsType<UploadFailed>(await uploader.UploadAsync(Png, cts.Token));

        Assert.Equal(UploadFailureKind.Cancelled, failed.Kind);
    }

    [Fact]
    public async Task Network_error_maps_to_network_failure()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("offline"));
        using var uploader = new ImgurUploader("id", handler);

        var failed = Assert.IsType<UploadFailed>(await uploader.UploadAsync(Png, CancellationToken.None));

        Assert.Equal(UploadFailureKind.Network, failed.Kind);
    }

    [Theory]
    [InlineData("""{"data":{"link":"http://i.imgur.com/abc.png"}}""")]
    [InlineData("""{"data":{}}""")]
    [InlineData("not json")]
    public async Task Unusable_success_body_is_rejected(string body)
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, body)));
        using var uploader = new ImgurUploader("id", handler);

        var failed = Assert.IsType<UploadFailed>(await uploader.UploadAsync(Png, CancellationToken.None));

        Assert.Equal(UploadFailureKind.Rejected, failed.Kind);
    }
}
