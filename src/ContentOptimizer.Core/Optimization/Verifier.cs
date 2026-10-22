using ContentOptimizer.Core.Imaging;
using ContentOptimizer.Core.Vtf;

namespace ContentOptimizer.Core.Optimization;

public sealed record VerifyResult(bool Ok, string? Error, double Psnr);

public static class Verifier
{
    public static VerifyResult Verify(byte[] src, VtfInfo srcInfo, byte[] dst, FilePlan plan)
    {
        var spec = plan.Spec!;
        if (!VtfParser.TryParse(dst, out var dstInfo, out string? error) || dstInfo == null)
        {
            return new VerifyResult(false, $"output unreadable: {error}", 0);
        }

        if (dst.Length != plan.OutputBytes)
        {
            return new VerifyResult(false, $"output size {dst.Length} differs from plan {plan.OutputBytes}", 0);
        }

        if (dstInfo.Width != plan.NewWidth || dstInfo.Height != plan.NewHeight || dstInfo.MipCount != plan.NewMipCount ||
            dstInfo.Format != spec.TargetFormat || dstInfo.Frames != srcInfo.Frames || dstInfo.Faces != srcInfo.Faces ||
            dstInfo.Flags != srcInfo.Flags)
        {
            return new VerifyResult(false, "output header mismatch", 0);
        }

        if (spec.DownscaleShift == 0 && spec.TargetFormat == srcInfo.Format)
        {
            var expected = src.AsSpan((int)srcInfo.ImageOffset, (int)dstInfo.ImageSize);
            var actual = dst.AsSpan((int)dstInfo.ImageOffset, (int)dstInfo.ImageSize);
            return expected.SequenceEqual(actual)
                ? new VerifyResult(true, null, double.PositiveInfinity)
                : new VerifyResult(false, "chopped image data differs from source mips", 0);
        }

        var acc = new ErrorAccumulator();
        int srcMip = spec.DropMips;
        int count = dstInfo.ImagesPerMip(0);
        int sw = srcInfo.MipWidth(srcMip);
        int sh = srcInfo.MipHeight(srcMip);
        for (int n = 0; n < count; n++)
        {
            byte[] reference = PixelCodec.Decode(srcInfo.Format, srcInfo.MipImage(src, srcMip, n), sw, sh);
            int rw = sw;
            int rh = sh;
            if (spec.DownscaleShift > 0)
            {
                reference = Resampler.BoxDownsample(reference, sw, sh, spec.DownscaleShift, out rw, out rh);
            }

            if (rw != dstInfo.Width || rh != dstInfo.Height)
            {
                return new VerifyResult(false, "reference size mismatch", 0);
            }

            byte[] actual = PixelCodec.Decode(dstInfo.Format, dstInfo.MipImage(dst, 0, n), rw, rh);
            acc.Add(reference, actual);
        }

        double psnr = acc.Psnr;
        return new VerifyResult(true, null, psnr);
    }
}
