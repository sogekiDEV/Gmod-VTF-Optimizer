using System.Buffers.Binary;
using ContentOptimizer.Core.Imaging;

namespace ContentOptimizer.Core.Vtf;

public sealed record VtfTransformSpec(int DropMips, int DownscaleShift, ImageFormat TargetFormat);

public static class VtfTransformer
{
    public static byte[] Apply(byte[] src, VtfInfo info, VtfTransformSpec spec)
    {
        int drop = spec.DropMips;
        int shift = spec.DownscaleShift;
        int newMips = shift > 0 ? 1 : info.MipCount - drop;
        int totalShift = drop + shift;
        int newWidth = Math.Max(1, info.Width >> totalShift);
        int newHeight = Math.Max(1, info.Height >> totalShift);
        int newDepth = Math.Max(1, info.Depth >> drop);
        long newImageSize = VtfLayout.ImageSize(newWidth, newHeight, newDepth, info.Frames, info.Faces, newMips, spec.TargetFormat);

        var image = new byte[newImageSize];
        BuildImage(src, info, spec, newWidth, newHeight, newMips, image);

        long oldEnd = info.ImageOffset + info.ImageSize;
        long delta = newImageSize - info.ImageSize;
        var dst = new byte[src.Length + delta];
        Buffer.BlockCopy(src, 0, dst, 0, (int)info.ImageOffset);
        Buffer.BlockCopy(image, 0, dst, (int)info.ImageOffset, image.Length);
        Buffer.BlockCopy(src, (int)oldEnd, dst, (int)(info.ImageOffset + newImageSize), (int)(src.Length - oldEnd));
        PatchHeader(dst, info, spec, newWidth, newHeight, newDepth, newMips, delta, oldEnd);
        return dst;
    }

    private static void BuildImage(byte[] src, VtfInfo info, VtfTransformSpec spec, int newWidth, int newHeight, int newMips, byte[] image)
    {
        int drop = spec.DropMips;
        int shift = spec.DownscaleShift;
        long dstOffset = 0;
        for (int j = newMips - 1; j >= 0; j--)
        {
            int srcMip = j + drop;
            int sw = info.MipWidth(srcMip);
            int sh = info.MipHeight(srcMip);
            int dw = Math.Max(1, newWidth >> j);
            int dh = Math.Max(1, newHeight >> j);
            int count = info.ImagesPerMip(srcMip);
            long srcImageBytes = info.MipImageBytes(srcMip);
            long dstImageBytes = ImageFormatInfo.ImageSize(spec.TargetFormat, dw, dh);
            for (int n = 0; n < count; n++)
            {
                var source = info.MipImage(src, srcMip, n);
                var target = image.AsSpan((int)dstOffset, (int)dstImageBytes);
                ConvertImage(source, info.Format, sw, sh, spec.TargetFormat, shift, target, dw, dh);
                dstOffset += dstImageBytes;
            }
        }
    }

    private static void ConvertImage(ReadOnlySpan<byte> source, ImageFormat from, int sw, int sh, ImageFormat to, int shift, Span<byte> target, int dw, int dh)
    {
        if (shift == 0 && from == to)
        {
            source.CopyTo(target);
            return;
        }

        if (shift == 0 && from == ImageFormat.Dxt5 && to == ImageFormat.Dxt1)
        {
            DxtDecoder.RepackDxt5ToDxt1(source, target, sw, sh);
            return;
        }

        byte[] rgba = PixelCodec.Decode(from, source, sw, sh);
        if (shift > 0)
        {
            rgba = Resampler.BoxDownsample(rgba, sw, sh, shift, out int nw, out int nh);
            if (nw != dw || nh != dh)
            {
                throw new InvalidOperationException("downscale size mismatch");
            }
        }

        PixelCodec.Encode(to, rgba, dw, dh, target);
    }

    private static void PatchHeader(byte[] dst, VtfInfo info, VtfTransformSpec spec, int width, int height, int depth, int mips, long delta, long oldEnd)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(dst.AsSpan(16), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(dst.AsSpan(18), (ushort)height);
        BinaryPrimitives.WriteInt32LittleEndian(dst.AsSpan(52), (int)spec.TargetFormat);
        dst[56] = (byte)mips;
        if (info.Minor >= 2)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(dst.AsSpan(63), (ushort)depth);
        }

        if (!info.HasResources)
        {
            return;
        }

        for (int i = 0; i < info.Resources.Length; i++)
        {
            var res = info.Resources[i];
            if (res.HasChunk && res.Offset >= oldEnd)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(dst.AsSpan(80 + 8 * i + 4), (uint)(res.Offset + delta));
            }
        }
    }
}
