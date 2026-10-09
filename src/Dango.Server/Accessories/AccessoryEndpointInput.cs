using Dango.AccessoryAssets;
using Dango.Protocol.Accessories;
using Dango.Server.Accounts;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace Dango.Server.Accessories;

internal static class AccessoryEndpointInput
{
    internal static async Task<CommitAccessoryOutfitRequest> ReadCommitAsync(HttpContext context,
        Func<CancellationToken, Task> authorize, CancellationToken ct)
    {
        await authorize(ct);
        RequireContentType(context.Request, "application/json", allowUtf8: true);
        LimitBody(context, AccessoryPersistence.MaximumJsonBytes);
        return AccessoryPersistence.ParseCommit(await ReadLimitedAsync(context.Request.Body, AccessoryPersistence.MaximumJsonBytes, ct));
    }

    internal static async Task<string> PreparePngAsync(HttpContext context, Func<CancellationToken, Task> authorize, CancellationToken ct)
    {
        await authorize(ct);
        RequireContentType(context.Request, "image/png", allowUtf8: false);
        var sourceDigest = context.Request.Headers["X-Accessory-Source-Digest"];
        if (sourceDigest.Count != 1 || !AccessoryOutfitItem.IsContentHash(sourceDigest[0])) throw new AccountFault("accessory_source_digest_invalid");
        LimitBody(context, AccessoryPngNormalizer.MaximumFileBytes);
        return sourceDigest[0]!;
    }

    private static void RequireContentType(HttpRequest request, string mediaType, bool allowUtf8)
    {
        if (request.Headers.ContentEncoding.Count != 0 || !MediaTypeHeaderValue.TryParse(request.ContentType, out var parsed)
            || !string.Equals(parsed.MediaType.Value, mediaType, StringComparison.OrdinalIgnoreCase)
            || parsed.Parameters.Any(p => !allowUtf8 || !string.Equals(p.Name.Value, "charset", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(p.Value.Value?.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase))
            || parsed.Parameters.Count > (allowUtf8 ? 1 : 0)) throw new AccountFault("accessory_content_type_invalid", 415);
    }

    private static void LimitBody(HttpContext context, long maximum)
    {
        if (context.Request.ContentLength > maximum) throw new AccountFault("accessory_body_too_large", 413);
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is not null)
        {
            if (feature.IsReadOnly) throw new AccountFault("accessory_body_limit_unavailable", 503);
            feature.MaxRequestBodySize = maximum;
        }
    }

    internal static async Task<byte[]> ReadLimitedAsync(Stream source, int maximum, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maximum - output.Length + 1)), deadline.Token);
                if (read == 0) break;
                if (output.Length + read > maximum) throw new AccountFault("accessory_body_too_large", 413);
                output.Write(buffer, 0, read);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AccountFault("accessory_body_timeout", 408); }
        catch (BadHttpRequestException error) when (error.StatusCode == 413) { throw new AccountFault("accessory_body_too_large", 413); }
        return output.ToArray();
    }
}
