using ContentOptimizer.Core.Imaging;
using ContentOptimizer.Core.Vtf;

namespace ContentOptimizer.Core.Optimization;

public sealed record FilePlan(
    bool Apply,
    string? Reason,
    VtfTransformSpec? Spec,
    int NewWidth,
    int NewHeight,
    int NewMipCount,
    long OutputBytes)
{
    public static FilePlan Skip(string reason) => new(false, reason, null, 0, 0, 0, 0);
}

public static class Planner
{
    public static FilePlan Plan(VtfInfo info, byte[] data, string relativePath, OptimizeOptions options)
    {
        if (options.IsExcluded(relativePath))
        {
            return FilePlan.Skip("excluded by pattern");
        }

        uint flags = info.Flags;
        if ((flags & (VtfFlags.RenderTarget | VtfFlags.DepthRenderTarget | VtfFlags.Procedural)) != 0)
        {
            return FilePlan.Skip("render target or procedural");
        }

        if (options.RespectNoLod && (flags & VtfFlags.NoLod) != 0)
        {
            return FilePlan.Skip("NOLOD flag");
        }

        int needed = 0;
        int trigger = Math.Max(options.MaxSize, options.TriggerSide);
        if (options.MaxSize > 0 && Math.Max(info.Width, info.Height) > trigger)
        {
            while ((Math.Max(info.Width, info.Height) >> needed) > options.MaxSize)
            {
                needed++;
            }
        }

        int drop = 0;
        int shift = 0;
        if (needed > 0)
        {
            if (info.MipCount > 1)
            {
                drop = Math.Min(needed, info.MipCount - 1);
            }
            else if (info.Depth == 1 && PixelCodec.CanDecode(info.Format) && PixelCodec.CanEncode(info.Format))
            {
                shift = needed;
            }
        }

        int totalShift = drop + shift;
        int newWidth = Math.Max(1, info.Width >> totalShift);
        int newHeight = Math.Max(1, info.Height >> totalShift);
        int newMips = shift > 0 ? 1 : info.MipCount - drop;
        int newDepth = Math.Max(1, info.Depth >> drop);

        ImageFormat target = info.Format;
        bool normal = (flags & VtfFlags.Normal) != 0;
        if (options.ConvertFormats && Math.Min(newWidth, newHeight) >= 4 && Math.Max(newWidth, newHeight) >= options.MinConvertSide &&
            (options.ConvertNormalMaps || !normal))
        {
            switch (info.Format)
            {
                case ImageFormat.Rgba8888:
                case ImageFormat.Abgr8888:
                case ImageFormat.Argb8888:
                case ImageFormat.Bgra8888:
                    target = options.DetectOpaque && IsOpaqueFrom(info, data, drop) ? ImageFormat.Dxt1 : ImageFormat.Dxt5;
                    break;
                case ImageFormat.Rgb888:
                case ImageFormat.Bgr888:
                case ImageFormat.Bgrx8888:
                    target = ImageFormat.Dxt1;
                    break;
                case ImageFormat.Dxt5:
                    if (options.DetectOpaque && IsOpaqueFrom(info, data, drop))
                    {
                        target = ImageFormat.Dxt1;
                    }

                    break;
            }
        }

        if (drop == 0 && shift == 0 && target == info.Format)
        {
            return FilePlan.Skip(needed > 0 ? "mip chain too short to reduce" : "already optimal");
        }

        long output = info.Length - info.ImageSize +
                      VtfLayout.ImageSize(newWidth, newHeight, newDepth, info.Frames, info.Faces, newMips, target);
        if (output >= info.Length)
        {
            return FilePlan.Skip("no size gain");
        }

        var spec = new VtfTransformSpec(drop, shift, target);
        return new FilePlan(true, null, spec, newWidth, newHeight, newMips, output);
    }

    private static bool IsOpaqueFrom(VtfInfo info, byte[] data, int firstMip)
    {
        for (int mip = firstMip; mip < info.MipCount; mip++)
        {
            if (!AlphaScan.IsOpaque(info.Format, info.MipRegion(data, mip)))
            {
                return false;
            }
        }

        return true;
    }
}

public static class AlphaScan
{
    public static bool IsOpaque(ImageFormat format, ReadOnlySpan<byte> data)
    {
        switch (format)
        {
            case ImageFormat.Rgba8888:
            case ImageFormat.Bgra8888:
                for (int i = 3; i < data.Length; i += 4)
                {
                    if (data[i] != 255)
                    {
                        return false;
                    }
                }

                return true;
            case ImageFormat.Abgr8888:
            case ImageFormat.Argb8888:
                for (int i = 0; i < data.Length; i += 4)
                {
                    if (data[i] != 255)
                    {
                        return false;
                    }
                }

                return true;
            case ImageFormat.Dxt5:
                for (int i = 0; i + 16 <= data.Length; i += 16)
                {
                    if (!DxtDecoder.IsDxt5BlockOpaque(data.Slice(i, 8)))
                    {
                        return false;
                    }
                }

                return true;
            default:
                return false;
        }
    }
}
