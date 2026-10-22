using System.Globalization;
using System.Text;
using ContentOptimizer.Core.Imaging;
using ContentOptimizer.Core.Optimization;
using ContentOptimizer.Core.Vtf;

namespace ContentOptimizer.Cli;

internal static class Program
{
    private const string Usage = @"vtfopt - Garry's Mod VTF optimizer

  vtfopt scan <path>
      Format and size statistics, no changes.

  vtfopt optimize <path> [options]
      Dry run by default: computes exact sizes, writes nothing.

      --max <N>             Longest side cap in pixels (default 1024, 0 = no cap)
      --trigger <N>         Only reduce textures whose longest side exceeds N (default = --max)
      --no-convert          Keep pixel formats (no RGBA -> DXT)
      --no-opaque-detect    Never switch opaque textures to DXT1
      --skip-normal-maps    Do not convert textures flagged as normal maps
      --min-convert-side <N> Leave formats alone below this longest side (default 64)
      --ignore-nolod        Also process textures with the NOLOD flag
      --min-psnr <dB>       Quality guard, skip files below it (default 27)
      --exclude <glob>      Extra exclude pattern (repeatable)
      --include <glob>      Only process matching files (repeatable)
      --no-default-excludes Drop built-in excludes (vgui, hud, gui, console, spawnicons)
      --threads <N>         Worker threads (default: all cores)
      --report <file.csv>   Write per-file CSV report

      --apply --out <dir>       Write optimized files into a mirror folder
      --apply --backup <dir>    Optimize in place, originals saved in <dir>
      --copy-unchanged          With --out: also copy untouched VTFs and every other file (full addon copy)

  vtfopt preview <file.vtf> <out.png> [--mip N]
      Decode one mip level to a PNG.";

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "scan" => Scan(args),
                "optimize" => Optimize(args),
                "preview" => Preview(args),
                _ => Fail($"unknown command '{args[0]}'"),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("run 'vtfopt help' for usage");
        return 2;
    }

    private static string Mb(long bytes) => (bytes / 1048576.0).ToString("F1", CultureInfo.InvariantCulture) + " MB";

    private static int Scan(string[] args)
    {
        if (args.Length < 2)
        {
            return Fail("scan needs a path");
        }

        var files = EnumerateVtf(args[1]);
        var byFormat = new SortedDictionary<string, (int Count, long Bytes)>();
        var byDim = new SortedDictionary<int, int>();
        int bad = 0;
        long total = 0;
        foreach (string file in files)
        {
            byte[] data = File.ReadAllBytes(file);
            if (!VtfParser.TryParse(data, out var info, out _) || info == null)
            {
                bad++;
                continue;
            }

            total += data.Length;
            string name = info.Format.ToString();
            byFormat.TryGetValue(name, out var cur);
            byFormat[name] = (cur.Count + 1, cur.Bytes + data.Length);
            int dim = Math.Max(info.Width, info.Height);
            byDim.TryGetValue(dim, out int c);
            byDim[dim] = c + 1;
        }

        Console.WriteLine($"{files.Count} files, {bad} unreadable, {Mb(total)}");
        Console.WriteLine();
        Console.WriteLine("format              files        size");
        foreach (var kv in byFormat.OrderByDescending(k => k.Value.Bytes))
        {
            Console.WriteLine($"{kv.Key,-18} {kv.Value.Count,6} {Mb(kv.Value.Bytes),11}");
        }

        Console.WriteLine();
        Console.WriteLine("longest side   files");
        foreach (var kv in byDim)
        {
            Console.WriteLine($"{kv.Key,12} {kv.Value,7}");
        }

        return 0;
    }

    private static List<string> EnumerateVtf(string path)
    {
        if (File.Exists(path))
        {
            return new List<string> { path };
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(path);
        }

        return Directory.EnumerateFiles(path, "*.vtf", SearchOption.AllDirectories).ToList();
    }

    private static int Optimize(string[] args)
    {
        if (args.Length < 2)
        {
            return Fail("optimize needs a path");
        }

        string input = args[1];
        var options = new OptimizeOptions();
        var settings = new RunSettings();
        var excludes = new List<string>(OptimizeOptions.DefaultExcludes);
        var includes = new List<string>();
        string? report = null;
        bool apply = false;

        for (int i = 2; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value");
            switch (a)
            {
                case "--max": options.MaxSize = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--trigger": options.TriggerSide = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--no-convert": options.ConvertFormats = false; break;
                case "--no-opaque-detect": options.DetectOpaque = false; break;
                case "--skip-normal-maps": options.ConvertNormalMaps = false; break;
                case "--min-convert-side": options.MinConvertSide = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--ignore-nolod": options.RespectNoLod = false; break;
                case "--min-psnr": options.QualityFloorDb = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--exclude": excludes.Add(Next()); break;
                case "--include": includes.Add(Next()); break;
                case "--no-default-excludes": excludes.Clear(); break;
                case "--threads": settings.Threads = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--report": report = Next(); break;
                case "--apply": apply = true; break;
                case "--out": settings.OutputDir = Next(); break;
                case "--backup": settings.BackupDir = Next(); break;
                case "--copy-unchanged": settings.CopyUnchanged = true; break;
                default: return Fail($"unknown option '{a}'");
            }
        }

        options.Excludes = excludes.ToArray();
        options.Includes = includes.ToArray();

        if (apply)
        {
            if (settings.OutputDir != null && settings.BackupDir != null)
            {
                return Fail("use either --out or --backup, not both");
            }

            if (settings.OutputDir != null)
            {
                settings.Mode = RunMode.Mirror;
            }
            else if (settings.BackupDir != null)
            {
                settings.Mode = RunMode.InPlace;
            }
            else
            {
                return Fail("--apply needs --out <dir> or --backup <dir>");
            }
        }
        else if (settings.OutputDir != null || settings.BackupDir != null)
        {
            return Fail("--out / --backup only make sense with --apply");
        }

        Console.WriteLine(settings.Mode == RunMode.DryRun
            ? "DRY RUN - nothing is written"
            : settings.Mode == RunMode.Mirror ? $"APPLY -> mirror {settings.OutputDir}" : $"APPLY in place, backup {settings.BackupDir}");

        int lastPercent = -1;
        var progress = new Progress<RunProgress>(p =>
        {
            int percent = p.Total == 0 ? 100 : p.Done * 100 / p.Total;
            if (percent != lastPercent && percent % 5 == 0)
            {
                lastPercent = percent;
                Console.Error.Write($"\r{percent,3}% ({p.Done}/{p.Total})");
            }
        });

        var result = OptimizerRunner.Run(input, options, settings, progress, CancellationToken.None);
        Console.Error.WriteLine();
        PrintSummary(result);
        if (report != null)
        {
            ReportCsv.Write(report, result);
            Console.WriteLine($"report: {report}");
        }

        return result.Count(FileStatus.Failed) > 0 ? 3 : 0;
    }

    private static void PrintSummary(RunReport r)
    {
        Console.WriteLine();
        Console.WriteLine($"files {r.Files.Count}   optimized {r.Count(FileStatus.Optimized)}   skipped {r.Count(FileStatus.Skipped)}   failed {r.Count(FileStatus.Failed)}   ({r.Elapsed.TotalSeconds:F1}s)");
        double pct = r.TotalBefore == 0 ? 0 : 100.0 * r.TotalSaved / r.TotalBefore;
        Console.WriteLine($"before {Mb(r.TotalBefore)}  ->  after {Mb(r.TotalAfter)}   saved {Mb(r.TotalSaved)} ({pct:F1}%)");

        var reasons = r.Files.Where(f => f.Status == FileStatus.Skipped)
            .GroupBy(f => f.Note ?? "?")
            .Select(g => (Reason: g.Key.StartsWith("quality guard") ? "quality guard" : g.Key, Count: g.Count()))
            .GroupBy(x => x.Reason)
            .Select(g => (Reason: g.Key, Count: g.Sum(x => x.Count)))
            .OrderByDescending(x => x.Count);
        Console.WriteLine();
        Console.WriteLine("skipped because:");
        foreach (var (reason, count) in reasons)
        {
            Console.WriteLine($"  {count,6}  {reason}");
        }

        var actions = r.Files.Where(f => f.Status == FileStatus.Optimized)
            .GroupBy(f => f.Note ?? "?")
            .OrderByDescending(g => g.Sum(f => f.Saved))
            .Take(8);
        Console.WriteLine();
        Console.WriteLine("what changed (by saved size):");
        foreach (var g in actions)
        {
            Console.WriteLine($"  {g.Count(),6} files  {Mb(g.Sum(f => f.Saved)),10}  {g.Key}");
        }

        Console.WriteLine();
        Console.WriteLine("top 10 savings:");
        foreach (var f in r.Files.Where(f => f.Status == FileStatus.Optimized).OrderByDescending(f => f.Saved).Take(10))
        {
            Console.WriteLine($"  {Mb(f.Saved),10}  {f.WidthBefore}x{f.HeightBefore} {f.FormatBefore} -> {f.WidthAfter}x{f.HeightAfter} {f.FormatAfter}  {f.RelativePath}");
        }

        var failed = r.Files.Where(f => f.Status == FileStatus.Failed).ToList();
        if (failed.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"failed ({failed.Count}):");
            foreach (var f in failed.Take(20))
            {
                Console.WriteLine($"  {f.RelativePath}: {f.Note}");
            }
        }
    }

    private static int Preview(string[] args)
    {
        if (args.Length < 3)
        {
            return Fail("preview needs <file.vtf> <out.png>");
        }

        int mip = 0;
        for (int i = 3; i < args.Length; i++)
        {
            if (args[i] == "--mip" && i + 1 < args.Length)
            {
                mip = int.Parse(args[++i], CultureInfo.InvariantCulture);
            }
        }

        byte[] data = File.ReadAllBytes(args[1]);
        if (!VtfParser.TryParse(data, out var info, out string? error) || info == null)
        {
            return Fail($"cannot read VTF: {error}");
        }

        if (mip < 0 || mip >= info.MipCount)
        {
            return Fail($"mip must be between 0 and {info.MipCount - 1}");
        }

        if (!PixelCodec.CanDecode(info.Format))
        {
            return Fail($"format {info.Format} cannot be previewed");
        }

        int w = info.MipWidth(mip);
        int h = info.MipHeight(mip);
        byte[] rgba = PixelCodec.Decode(info.Format, info.MipImage(data, mip, 0), w, h);
        PngWriter.Write(args[2], rgba, w, h);
        Console.WriteLine($"{info.VersionText} {info.Format} {info.Width}x{info.Height} mips={info.MipCount} frames={info.Frames} -> {w}x{h} {args[2]}");
        return 0;
    }
}
