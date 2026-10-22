using System.Globalization;
using System.Text;

namespace ContentOptimizer.Core.Optimization;

public static class ReportCsv
{
    public static void Write(string path, RunReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("path,status,note,before_bytes,after_bytes,width_before,height_before,width_after,height_after,format_before,format_after,psnr_db");
        foreach (var f in report.Files)
        {
            sb.Append(Escape(f.RelativePath)).Append(',')
              .Append(f.Status).Append(',')
              .Append(Escape(f.Note ?? "")).Append(',')
              .Append(f.BeforeBytes).Append(',')
              .Append(f.AfterBytes).Append(',')
              .Append(f.WidthBefore).Append(',')
              .Append(f.HeightBefore).Append(',')
              .Append(f.WidthAfter).Append(',')
              .Append(f.HeightAfter).Append(',')
              .Append(f.FormatBefore).Append(',')
              .Append(f.FormatAfter).Append(',')
              .AppendLine(double.IsNaN(f.Psnr) || double.IsInfinity(f.Psnr) ? "" : f.Psnr.ToString("F2", CultureInfo.InvariantCulture));
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
