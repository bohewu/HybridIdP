namespace Infrastructure.Services;

// Shared only by the three selected upstream JSON consumers.
internal static class BoundedUpstreamResponse
{
    public static async Task<HttpContent> ReadAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maximumBytes)
        {
            throw new HttpRequestException("Upstream response exceeds the byte limit.");
        }

        var buffer = new byte[maximumBytes + 1];
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var count = 0;
        while (count < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (read == 0) break;
            count += read;
        }

        if (count > maximumBytes)
        {
            throw new HttpRequestException("Upstream response exceeds the byte limit.");
        }

        var bounded = new ByteArrayContent(buffer, 0, count);
        // Retain ReadFromJsonAsync's existing charset handling, without media-type enforcement.
        bounded.Headers.ContentType = content.Headers.ContentType;
        return bounded;
    }
}
