#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NetCommerce.Kernel.Core.Results;

namespace NetCommerce.Media.Application.Services;

public readonly record struct ValidatedImage(
    string MimeType,
    string FileExtension,
    string SafeFileName);

/// <summary>
/// 2026 Production-Ready Image Format Inspector.
/// Uses C# 13 ReadOnlySpan property compiler optimizations.
/// </summary>
public static class ImageInspector
{
    public const long MaxSizeBytes = 10 * 1024 * 1024; // 10MB
    private const int HeaderBytes = 12;

    // C# 13 / .NET 10: ReadOnlySpan properties store data directly in assembly RVA memory
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];
    private static ReadOnlySpan<byte> Gif87aSignature => [0x47, 0x49, 0x46, 0x38, 0x37, 0x61];
    private static ReadOnlySpan<byte> Gif89aSignature => [0x47, 0x49, 0x46, 0x38, 0x39, 0x61];
    private static ReadOnlySpan<byte> RiffHeader => [0x52, 0x49, 0x46, 0x46]; // 'RIFF'
    private static ReadOnlySpan<byte> WebpHeader => [0x57, 0x45, 0x42, 0x50]; // 'WEBP' at offset 8..11

    public static async Task<Result<ValidatedImage>> ValidateAsync(
        Stream stream,
        long declaredLength,
        CancellationToken ct = default)
    {
        if (declaredLength <= 0)
            return Result.Failure<ValidatedImage>(Error.Validation("File cannot be empty."));

        if (declaredLength > MaxSizeBytes)
            return Result.Failure<ValidatedImage>(Error.Validation("File size exceeds 10MB limit."));

        // 1. გამოვიყენოთ Memory<byte> await ოპერაციისთვის (CS4007-ის პრევენცია)
        byte[] buffer = new byte[HeaderBytes];
        var read = await stream.ReadAsync(buffer.AsMemory(0, HeaderBytes), ct);

        // 2. გადავახვიოთ სტრიმი თავში ატვირთვისთვის
        if (stream.CanSeek)
            stream.Position = 0;

        if (read < HeaderBytes)
            return Result.Failure<ValidatedImage>(Error.Validation("Invalid or corrupted file header."));

        // 3. ReadOnlySpan იქმნება await-ის შემდეგ (არ გადაკვეთს await საზღვარს)
        ReadOnlySpan<byte> header = buffer.AsSpan(0, read);

        if (header.StartsWith(PngSignature))
            return Result.Success(new ValidatedImage("image/png", ".png", $"{Guid.NewGuid():N}.png"));

        if (header.StartsWith(JpegSignature))
            return Result.Success(new ValidatedImage("image/jpeg", ".jpg", $"{Guid.NewGuid():N}.jpg"));

        // Strict WebP check: RIFF at 0..3 AND WEBP at 8..11
        if (header.StartsWith(RiffHeader) && header.Slice(8, 4).SequenceEqual(WebpHeader))
            return Result.Success(new ValidatedImage("image/webp", ".webp", $"{Guid.NewGuid():N}.webp"));

        if (header.StartsWith(Gif87aSignature) || header.StartsWith(Gif89aSignature))
            return Result.Success(new ValidatedImage("image/gif", ".gif", $"{Guid.NewGuid():N}.gif"));

        return Result.Failure<ValidatedImage>(Error.Validation("Unsupported format. Allowed: JPEG, PNG, WEBP, GIF."));
    }
}
