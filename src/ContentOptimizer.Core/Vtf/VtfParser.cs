using System.Buffers.Binary;

namespace ContentOptimizer.Core.Vtf;

public static class VtfParser
{
    public static bool TryParse(ReadOnlySpan<byte> d, out VtfInfo? info, out string? error)
    {
        info = null;
        error = null;

        if (d.Length < 64)
        {
            error = "file too short";
            return false;
        }

        if (d[0] != (byte)'V' || d[1] != (byte)'T' || d[2] != (byte)'F' || d[3] != 0)
        {
            error = "not a VTF file";
            return false;
        }

        uint major = BinaryPrimitives.ReadUInt32LittleEndian(d[4..]);
        uint minor = BinaryPrimitives.ReadUInt32LittleEndian(d[8..]);
        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(d[12..]);
        if (major != 7 || minor > 5)
        {
            error = $"unsupported version {major}.{minor}";
            return false;
        }

        if (headerSize < 64 || headerSize > d.Length)
        {
            error = "invalid header size";
            return false;
        }

        int width = BinaryPrimitives.ReadUInt16LittleEndian(d[16..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(d[18..]);
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(d[20..]);
        int frames = BinaryPrimitives.ReadUInt16LittleEndian(d[24..]);
        int firstFrame = BinaryPrimitives.ReadUInt16LittleEndian(d[26..]);
        int formatValue = BinaryPrimitives.ReadInt32LittleEndian(d[52..]);
        int mipCount = d[56];
        int lowResValue = BinaryPrimitives.ReadInt32LittleEndian(d[57..]);
        int lowResWidth = d[61];
        int lowResHeight = d[62];
        int depth = 1;
        if (minor >= 2 && d.Length >= 65)
        {
            depth = Math.Max(1, (int)BinaryPrimitives.ReadUInt16LittleEndian(d[63..]));
        }

        if (width < 1 || height < 1 || frames < 1 || mipCount < 1 || mipCount > 16)
        {
            error = "invalid dimensions or counts";
            return false;
        }

        if (!ImageFormatInfo.IsKnown(formatValue))
        {
            error = $"unsupported image format {formatValue}";
            return false;
        }

        var format = (ImageFormat)formatValue;
        int faces = 1;
        if ((flags & VtfFlags.EnvMap) != 0)
        {
            faces = minor < 5 && firstFrame != 0xFFFF ? 7 : 6;
        }

        long thumbSize = 0;
        if (ImageFormatInfo.IsKnown(lowResValue) && lowResWidth > 0 && lowResHeight > 0)
        {
            thumbSize = ImageFormatInfo.ImageSize((ImageFormat)lowResValue, lowResWidth, lowResHeight);
        }

        var resources = Array.Empty<VtfResource>();
        long thumbOffset = headerSize;
        long imageOffset;

        if (minor >= 3)
        {
            if (d.Length < 80)
            {
                error = "file too short for resources";
                return false;
            }

            uint count = BinaryPrimitives.ReadUInt32LittleEndian(d[68..]);
            if (count > 64 || 80 + 8 * (long)count > d.Length)
            {
                error = "invalid resource count";
                return false;
            }

            resources = new VtfResource[count];
            bool hasThumb = false;
            bool hasImage = false;
            thumbOffset = 0;
            imageOffset = 0;
            for (int i = 0; i < count; i++)
            {
                int p = 80 + 8 * i;
                uint tag = (uint)(d[p] | (d[p + 1] << 8) | (d[p + 2] << 16));
                byte rflags = d[p + 3];
                uint offset = BinaryPrimitives.ReadUInt32LittleEndian(d[(p + 4)..]);
                resources[i] = new VtfResource(tag, rflags, offset);
                if (tag == VtfResource.ThumbnailTag)
                {
                    hasThumb = true;
                    thumbOffset = offset;
                }
                else if (tag == VtfResource.ImageTag)
                {
                    hasImage = true;
                    imageOffset = offset;
                }
            }

            if (!hasImage)
            {
                thumbOffset = headerSize;
                imageOffset = thumbOffset + thumbSize;
            }
            else if (!hasThumb)
            {
                thumbSize = 0;
            }
        }
        else
        {
            imageOffset = thumbOffset + thumbSize;
        }

        long imageSize = VtfLayout.ImageSize(width, height, depth, frames, faces, mipCount, format);
        if (imageOffset < headerSize || imageOffset + imageSize > d.Length)
        {
            error = "truncated image data";
            return false;
        }

        info = new VtfInfo
        {
            Major = (int)major,
            Minor = (int)minor,
            HeaderSize = (int)headerSize,
            Width = width,
            Height = height,
            Flags = flags,
            Frames = frames,
            FirstFrame = firstFrame,
            Format = format,
            MipCount = mipCount,
            LowResWidth = lowResWidth,
            LowResHeight = lowResHeight,
            Depth = depth,
            Faces = faces,
            Resources = resources,
            ThumbOffset = thumbOffset,
            ThumbSize = thumbSize,
            ImageOffset = imageOffset,
            ImageSize = imageSize,
            Length = d.Length,
        };
        return true;
    }
}
