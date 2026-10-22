namespace ContentOptimizer.Core.Imaging;

public static class Resampler
{
    public static byte[] BoxDownsample(byte[] rgba, int width, int height, int shift, out int newWidth, out int newHeight)
    {
        byte[] current = rgba;
        int w = width;
        int h = height;
        for (int s = 0; s < shift; s++)
        {
            int nw = Math.Max(1, w / 2);
            int nh = Math.Max(1, h / 2);
            var next = new byte[nw * nh * 4];
            for (int y = 0; y < nh; y++)
            {
                int y0 = Math.Min(y * 2, h - 1);
                int y1 = Math.Min(y * 2 + 1, h - 1);
                for (int x = 0; x < nw; x++)
                {
                    int x0 = Math.Min(x * 2, w - 1);
                    int x1 = Math.Min(x * 2 + 1, w - 1);
                    int p0 = (y0 * w + x0) * 4;
                    int p1 = (y0 * w + x1) * 4;
                    int p2 = (y1 * w + x0) * 4;
                    int p3 = (y1 * w + x1) * 4;
                    int a0 = current[p0 + 3];
                    int a1 = current[p1 + 3];
                    int a2 = current[p2 + 3];
                    int a3 = current[p3 + 3];
                    int sumA = a0 + a1 + a2 + a3;
                    int d = (y * nw + x) * 4;
                    for (int c = 0; c < 3; c++)
                    {
                        int v;
                        if (sumA == 0)
                        {
                            v = (current[p0 + c] + current[p1 + c] + current[p2 + c] + current[p3 + c] + 2) / 4;
                        }
                        else
                        {
                            long weighted = (long)current[p0 + c] * a0 + (long)current[p1 + c] * a1 +
                                            (long)current[p2 + c] * a2 + (long)current[p3 + c] * a3;
                            v = (int)((weighted + sumA / 2) / sumA);
                        }

                        next[d + c] = (byte)v;
                    }

                    next[d + 3] = (byte)((sumA + 2) / 4);
                }
            }

            current = next;
            w = nw;
            h = nh;
        }

        newWidth = w;
        newHeight = h;
        return current;
    }
}
