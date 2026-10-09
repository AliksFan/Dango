using System.Text;
using Dango.Server.Accessories;
using Dango.Server.Accounts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Accessories;

[TestClass]
public sealed class AccessoryEndpointInputTests
{
    [TestMethod]
    public async Task AuthenticationMustSucceedBeforeAnyBodyReadForBothMutationEndpoints()
    {
        foreach (var png in new[] { false, true })
        {
            var body = new ReadObserverStream(Encoding.UTF8.GetBytes(AccessoryPersistenceTests.Wire()));
            var context = Context(body, png ? "image/png" : "application/json");
            var fault = await Assert.ThrowsExactlyAsync<AccountFault>(async () =>
            {
                if (png) await AccessoryEndpointInput.PreparePngAsync(context, Deny, CancellationToken.None);
                else await AccessoryEndpointInput.ReadCommitAsync(context, Deny, CancellationToken.None);
            });
            Assert.AreEqual("authentication_required", fault.Code);
            Assert.AreEqual(0, body.Reads);
        }
        static Task Deny(CancellationToken _) => throw new AccountFault("authentication_required", 401);
    }

    [TestMethod]
    public async Task OnlyAccessoryBodiesChangeTheirOwnServerBudget()
    {
        var context = Context(new MemoryStream(Encoding.UTF8.GetBytes(AccessoryPersistenceTests.Wire())), "application/json; charset=utf-8");
        var feature = new BodyBudget(); context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
        var parsed = await AccessoryEndpointInput.ReadCommitAsync(context, Allow, CancellationToken.None);
        Assert.AreEqual(AccessoryPersistenceTests.Request(), parsed);
        Assert.AreEqual(32 * 1024L, feature.MaxRequestBodySize);
        var pngContext = Context(new MemoryStream(), "image/png");
        var pngFeature = new BodyBudget(); pngContext.Features.Set<IHttpMaxRequestBodySizeFeature>(pngFeature);
        Assert.AreEqual(new string('a', 64), await AccessoryEndpointInput.PreparePngAsync(pngContext, Allow, CancellationToken.None));
        Assert.AreEqual(32 * 1024 * 1024L, pngFeature.MaxRequestBodySize);
        Assert.AreEqual(8192L, new BodyBudget().MaxRequestBodySize);
    }

    [TestMethod]
    [DataRow("application/json", true)]
    [DataRow("image/png; charset=utf-8", true)]
    [DataRow("text/json", false)]
    [DataRow("application/json; charset=utf-16", false)]
    public async Task UnexpectedMediaTypesAndImageParametersAreRejectedWithoutReading(string contentType, bool png)
    {
        var body = new ReadObserverStream([]); var context = Context(body, contentType);
        var fault = await Assert.ThrowsExactlyAsync<AccountFault>(async () =>
        {
            if (png) await AccessoryEndpointInput.PreparePngAsync(context, Allow, CancellationToken.None);
            else await AccessoryEndpointInput.ReadCommitAsync(context, Allow, CancellationToken.None);
        });
        Assert.AreEqual(415, fault.Status); Assert.AreEqual(0, body.Reads);
    }

    [TestMethod]
    public async Task EncodedBodiesAndClaimedHashMultiplicityAreRejected()
    {
        var context = Context(new MemoryStream(), "image/png"); context.Request.Headers.ContentEncoding = "gzip";
        Assert.AreEqual(415, (await Assert.ThrowsExactlyAsync<AccountFault>(() => AccessoryEndpointInput.PreparePngAsync(context, Allow, CancellationToken.None))).Status);
        context.Request.Headers.Remove("Content-Encoding"); context.Request.Headers["X-Accessory-Source-Digest"] = new[] { new string('a', 64), new string('a', 64) };
        Assert.AreEqual("accessory_source_digest_invalid", (await Assert.ThrowsExactlyAsync<AccountFault>(() => AccessoryEndpointInput.PreparePngAsync(context, Allow, CancellationToken.None))).Code);
    }

    [TestMethod]
    public async Task BothDeclaredAndStreamingOversizeBodiesFailWithinBudget()
    {
        var body = new ReadObserverStream(new byte[AccessoryPersistence.MaximumJsonBytes + 1024]);
        var context = Context(body, "application/json"); context.Request.ContentLength = AccessoryPersistence.MaximumJsonBytes + 1;
        Assert.AreEqual(413, (await Assert.ThrowsExactlyAsync<AccountFault>(() => AccessoryEndpointInput.ReadCommitAsync(context, Allow, CancellationToken.None))).Status);
        Assert.AreEqual(0, body.Reads);
        context.Request.ContentLength = null;
        Assert.AreEqual(413, (await Assert.ThrowsExactlyAsync<AccountFault>(() => AccessoryEndpointInput.ReadCommitAsync(context, Allow, CancellationToken.None))).Status);
        Assert.AreEqual(AccessoryPersistence.MaximumJsonBytes + 1L, body.Position);
    }

    private static Task Allow(CancellationToken _) => Task.CompletedTask;
    private static DefaultHttpContext Context(Stream body, string contentType)
    {
        var result = new DefaultHttpContext(); result.Request.Body = body; result.Request.ContentType = contentType;
        result.Request.Headers["X-Accessory-Source-Digest"] = new string('a', 64); return result;
    }
    private sealed class BodyBudget : IHttpMaxRequestBodySizeFeature
    { public bool IsReadOnly => false; public long? MaxRequestBodySize { get; set; } = 8192; }
    private sealed class ReadObserverStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int Reads { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { Reads++; return base.ReadAsync(buffer, ct); }
    }
}
