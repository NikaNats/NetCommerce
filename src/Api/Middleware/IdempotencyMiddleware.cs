using System.Buffers;
using NetCommerce.SharedKernel.Infrastructure;

namespace NetCommerce.Api.Middleware;

/// <summary>
/// Middleware for idempotency key processing on write operations.
/// Prevents duplicate processing of the same request.
/// Uses ArrayPool to minimize large object heap allocations.
/// </summary>
public class IdempotencyMiddleware
{
    private const string IdempotencyKeyHeader = "X-Idempotency-Key";
    private const int MaxCacheableResponseSize = 64 * 1024; // 64KB limit to avoid LOH
    private readonly RequestDelegate _next;
    private readonly ILogger<IdempotencyMiddleware> _logger;

    // HTTP methods that require idempotency
    private static readonly HashSet<string> IdempotentMethods = ["POST", "PUT", "PATCH", "DELETE"];

    public IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IIdempotencyService idempotencyService)
    {
        // Skip for non-write operations - fast path
        if (!IdempotentMethods.Contains(context.Request.Method))
        {
            await _next(context);
            return;
        }

        // Check for idempotency key
        if (!context.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var idempotencyKey) 
            || string.IsNullOrWhiteSpace(idempotencyKey))
        {
            // For development, you might want to allow requests without the key
            // In production, consider returning 400 Bad Request
            _logger.LogWarning(
                "Request to {Path} missing idempotency key",
                context.Request.Path);
            
            await _next(context);
            return;
        }

        var key = $"{context.Request.Path}:{idempotencyKey}";

        // Check if already processed
        var cachedResponse = await idempotencyService.GetAsync(key);
        if (cachedResponse != null)
        {
            _logger.LogInformation(
                "Returning cached response for idempotency key: {Key}",
                key);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = 200;
            await context.Response.WriteAsync(cachedResponse);
            return;
        }

        // Capture the response using pooled memory to avoid LOH allocations
        var originalBodyStream = context.Response.Body;
        
        // Use RecyclableMemoryStream pattern with ArrayPool
        await using var responseBody = new PooledMemoryStream();
        context.Response.Body = responseBody;

        await _next(context);

        // Cache successful responses that are within size limit
        if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
        {
            if (responseBody.Length <= MaxCacheableResponseSize)
            {
                responseBody.Position = 0;
                var responseContent = await new StreamReader(responseBody).ReadToEndAsync();
                
                // Fire and forget caching - don't block the response
                _ = idempotencyService.SetAsync(
                    key, 
                    responseContent, 
                    TimeSpan.FromHours(24));

                responseBody.Position = 0;
            }
            else
            {
                _logger.LogWarning(
                    "Response too large to cache for idempotency key: {Key}, Size: {Size}",
                    key,
                    responseBody.Length);
            }
        }

        responseBody.Position = 0;
        await responseBody.CopyToAsync(originalBodyStream);
    }
}

/// <summary>
/// Memory stream that uses ArrayPool to avoid large object heap allocations.
/// </summary>
internal sealed class PooledMemoryStream : Stream
{
    private const int DefaultBufferSize = 4096;
    private byte[] _buffer;
    private int _length;
    private int _position;
    private bool _disposed;

    public PooledMemoryStream()
    {
        _buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);
    }

    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => !_disposed;
    public override long Length => _length;
    
    public override long Position
    {
        get => _position;
        set => _position = (int)value;
    }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var bytesToRead = Math.Min(count, _length - _position);
        if (bytesToRead <= 0) return 0;

        Buffer.BlockCopy(_buffer, _position, buffer, offset, bytesToRead);
        _position += bytesToRead;
        return bytesToRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin => (int)offset,
            SeekOrigin.Current => _position + (int)offset,
            SeekOrigin.End => _length + (int)offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        return _position;
    }

    public override void SetLength(long value)
    {
        EnsureCapacity((int)value);
        _length = (int)value;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        EnsureCapacity(_position + count);
        Buffer.BlockCopy(buffer, offset, _buffer, _position, count);
        _position += count;
        _length = Math.Max(_length, _position);
    }

    private void EnsureCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= _buffer.Length) return;

        var newSize = Math.Max(requiredCapacity, _buffer.Length * 2);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
        Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _length);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = newBuffer;
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _disposed = true;
        }
        base.Dispose(disposing);
    }
}
