namespace ContentOptimizer.Core.Imaging;

public sealed class ErrorAccumulator
{
    private double sumSquares;
    private long count;

    public void Add(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        long local = 0;
        for (int i = 0; i < a.Length; i++)
        {
            int d = a[i] - b[i];
            local += d * d;
        }

        sumSquares += local;
        count += a.Length;
    }

    public double Psnr
    {
        get
        {
            if (count == 0 || sumSquares == 0)
            {
                return double.PositiveInfinity;
            }

            double mse = sumSquares / count;
            return 10.0 * Math.Log10(255.0 * 255.0 / mse);
        }
    }
}
