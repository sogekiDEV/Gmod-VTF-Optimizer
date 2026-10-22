using System;

namespace ContentOptimizer.App.Services
{
    public static class ByteFormat
    {
        private static readonly string[] UnitKeys = { "unit_b", "unit_kb", "unit_mb", "unit_gb", "unit_tb" };

        public static string Plain(long bytes)
        {
            double value = Math.Abs((double)bytes);
            int unit = 0;
            while (value >= 1024 && unit < UnitKeys.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            string number = unit == 0
                ? value.ToString("0", Loc.Instance.Culture)
                : value.ToString(value >= 100 ? "0" : "0.0", Loc.Instance.Culture);
            string sign = bytes < 0 ? "-" : string.Empty;
            return sign + number + " " + Loc.Instance.Get(UnitKeys[unit]);
        }

        public static string Format(long bytes) => Loc.Ltr(Plain(bytes));
    }
}
