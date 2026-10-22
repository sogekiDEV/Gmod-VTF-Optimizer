namespace ContentOptimizer.Core.Vtf;

public readonly record struct VtfResource(uint Tag, byte Flags, uint Offset)
{
    public const uint ThumbnailTag = 0x000001;
    public const uint ImageTag = 0x000030;
    public const byte InlineFlag = 0x2;

    public bool HasChunk => (Flags & InlineFlag) == 0;
}

public static class VtfLayout
{
    public static long ImageSize(int width, int height, int depth, int frames, int faces, int mipCount, ImageFormat format)
    {
        long total = 0;
        for (int mip = 0; mip < mipCount; mip++)
        {
            long one = ImageFormatInfo.ImageSize(format, Math.Max(1, width >> mip), Math.Max(1, height >> mip));
            total += one * frames * faces * Math.Max(1, depth >> mip);
        }

        return total;
    }
}

public sealed class VtfInfo
{
    public int Major { get; init; }
    public int Minor { get; init; }
    public int HeaderSize { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public uint Flags { get; init; }
    public int Frames { get; init; }
    public int FirstFrame { get; init; }
    public ImageFormat Format { get; init; }
    public int MipCount { get; init; }
    public int LowResWidth { get; init; }
    public int LowResHeight { get; init; }
    public int Depth { get; init; }
    public int Faces { get; init; }
    public VtfResource[] Resources { get; init; } = Array.Empty<VtfResource>();
    public long ThumbOffset { get; init; }
    public long ThumbSize { get; init; }
    public long ImageOffset { get; init; }
    public long ImageSize { get; init; }
    public long Length { get; init; }

    public bool HasResources => Minor >= 3;
    public bool IsEnvMap => (Flags & VtfFlags.EnvMap) != 0;
    public string VersionText => $"{Major}.{Minor}";

    public int MipWidth(int mip) => Math.Max(1, Width >> mip);
    public int MipHeight(int mip) => Math.Max(1, Height >> mip);
    public int MipDepth(int mip) => Math.Max(1, Depth >> mip);
    public int ImagesPerMip(int mip) => Frames * Faces * MipDepth(mip);
    public long MipImageBytes(int mip) => ImageFormatInfo.ImageSize(Format, MipWidth(mip), MipHeight(mip));
    public long MipRegionBytes(int mip) => MipImageBytes(mip) * ImagesPerMip(mip);

    public long MipOffset(int mip)
    {
        long offset = 0;
        for (int k = MipCount - 1; k > mip; k--)
        {
            offset += MipRegionBytes(k);
        }

        return offset;
    }

    public ReadOnlySpan<byte> MipRegion(byte[] data, int mip) =>
        data.AsSpan((int)(ImageOffset + MipOffset(mip)), (int)MipRegionBytes(mip));

    public ReadOnlySpan<byte> MipImage(byte[] data, int mip, int index) =>
        data.AsSpan((int)(ImageOffset + MipOffset(mip) + index * MipImageBytes(mip)), (int)MipImageBytes(mip));
}
