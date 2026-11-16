using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The still of a medium given to a task, read from the computer's picture route (ADR 0066, protocol 3).</summary>
public class TaskPictureTests
{
    private sealed class PictureHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode status;
        private readonly HttpContent content;

        public PictureHandler(HttpStatusCode status, HttpContent content)
        {
            this.status = status;
            this.content = content;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
        }
    }

    private static readonly byte[] Png = { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 1, 2, 3 };

    private static HttpContent Bytes(byte[] bytes, string type)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return content;
    }

    private static ControlPlaneApi Api(PictureHandler handler) => new(new Uri("http://127.0.0.1:47800/"), "test-token", handler);

    [Test]
    public void ThePictureIsReadByItsProjectJournalAndPathEachEscapedWithThePairedToken()
    {
        var handler = new PictureHandler(HttpStatusCode.OK, Bytes(Png, "image/png"));
        var bytes = Api(handler).GetPictureAsync("p1", "j 1", ".halora/media/kite clip.still.png").GetAwaiter().GetResult();
        Assert.That(bytes, Is.EqualTo(Png));
        var request = handler.Requests[0];
        Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
        Assert.That(request.RequestUri!.AbsoluteUri,
            Is.EqualTo("http://127.0.0.1:47800/api/projects/p1/picture?journal_id=j%201&path=.halora%2Fmedia%2Fkite%20clip.still.png"));
        Assert.That(request.Headers.Authorization?.Scheme, Is.EqualTo("Bearer"));
    }

    [Test]
    public void AMediumThatLeftTheProjectKeepsTheComputersCode()
    {
        var gone = new StringContent("{\"error\":{\"code\":\"media_gone\",\"message\":\"That photo or video is no longer in the project.\",\"issues\":[]}}");
        gone.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var refused = Assert.ThrowsAsync<ControlPlaneRequestException>(() => Api(new PictureHandler(HttpStatusCode.NotFound, gone)).GetPictureAsync("p1", "j1", "a.png"));
        Assert.That(refused!.Code, Is.EqualTo(TaskPicture.Gone));
    }

    [Test]
    public void OnlyAPngOrAJpegWithinTheLimitIsTakenAndNothingElseIsDrawn()
    {
        foreach (var (content, why) in new (HttpContent, string)[]
        {
            (Bytes(Png, "image/webp"), "a type the headset does not decode"),
            (Bytes(Png, "text/html"), "no picture at all"),
            (Bytes(new byte[0], "image/png"), "an empty body"),
            (Bytes(new byte[TaskPicture.MostBytes + 1], "image/jpeg"), "a body past the computer's limit"),
        })
        {
            var refused = Assert.ThrowsAsync<ControlPlaneRequestException>(() => Api(new PictureHandler(HttpStatusCode.OK, content)).GetPictureAsync("p1", "j1", "a.png"), why);
            Assert.That(refused!.Code, Is.EqualTo(TaskPicture.NotDrawn), why);
        }
        Assert.That(Api(new PictureHandler(HttpStatusCode.OK, Bytes(Png, "image/jpeg"))).GetPictureAsync("p1", "j1", "a.jpg").GetAwaiter().GetResult(), Is.EqualTo(Png));
    }

    [Test]
    public void ThePathRefusesAnEmptyPart()
    {
        Assert.Throws<ArgumentException>(() => TaskPicture.PathOf("", "j1", "a.png"));
        Assert.Throws<ArgumentException>(() => TaskPicture.PathOf("p1", "", "a.png"));
        Assert.Throws<ArgumentException>(() => TaskPicture.PathOf("p1", "j1", ""));
    }
}
