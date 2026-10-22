using System.Diagnostics;
using ContentOptimizer.Core.Vtf;

namespace ContentOptimizer.Core.Optimization;

public static class OptimizerRunner
{
    public static RunReport Run(string input, OptimizeOptions options, RunSettings settings, IProgress<RunProgress>? progress, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        var files = Enumerate(input, settings, out string root);
        if (settings.Mode == RunMode.Mirror && settings.OutputDir != null && SamePath(settings.OutputDir, root))
        {
            throw new ArgumentException("output folder must differ from the source folder");
        }

        var results = new FileResult[files.Count];
        int done = 0;
        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, settings.Threads),
            CancellationToken = ct,
        };

        Parallel.For(0, files.Count, parallel, i =>
        {
            string path = files[i];
            string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
            results[i] = ProcessFile(path, rel, options, settings);
            int d = Interlocked.Increment(ref done);
            progress?.Report(new RunProgress(d, files.Count, rel));
        });

        if (settings.Mode == RunMode.Mirror && settings.CopyUnchanged && settings.OutputDir != null && Directory.Exists(input))
        {
            progress?.Report(new RunProgress(files.Count, files.Count, "copie des autres fichiers"));
            CopyOtherFiles(root, settings, parallel);
        }

        timer.Stop();
        return new RunReport { Mode = settings.Mode, Elapsed = timer.Elapsed, Files = results };
    }

    public static EstimateResult Estimate(string input, IReadOnlyList<OptimizeOptions> variants, int threads, CancellationToken ct)
    {
        var files = Enumerate(input, new RunSettings(), out string root);
        long before = 0;
        var after = new long[variants.Count];
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threads), CancellationToken = ct };
        Parallel.ForEach(files, parallel, path =>
        {
            byte[] src;
            try
            {
                src = File.ReadAllBytes(path);
            }
            catch
            {
                return;
            }

            Interlocked.Add(ref before, src.Length);
            bool parsed = VtfParser.TryParse(src, out var info, out _) && info != null;
            string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
            for (int v = 0; v < variants.Count; v++)
            {
                long size = src.Length;
                if (parsed)
                {
                    try
                    {
                        var plan = Planner.Plan(info!, src, rel, variants[v]);
                        if (plan.Apply)
                        {
                            size = plan.OutputBytes;
                        }
                    }
                    catch
                    {
                    }
                }

                Interlocked.Add(ref after[v], size);
            }
        });

        return new EstimateResult(before, after);
    }

    private static List<string> Enumerate(string input, RunSettings settings, out string root)
    {
        if (File.Exists(input))
        {
            root = Path.GetDirectoryName(Path.GetFullPath(input))!;
            return new List<string> { Path.GetFullPath(input) };
        }

        root = Path.GetFullPath(input).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? outFull = settings.OutputDir != null ? Path.GetFullPath(settings.OutputDir) : null;
        string? backupFull = settings.BackupDir != null ? Path.GetFullPath(settings.BackupDir) : null;
        return Directory.EnumerateFiles(root, "*.vtf", SearchOption.AllDirectories)
            .Where(f => !IsUnder(f, outFull) && !IsUnder(f, backupFull))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static void CopyOtherFiles(string root, RunSettings settings, ParallelOptions parallel)
    {
        string outFull = Path.GetFullPath(settings.OutputDir!);
        var others = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase) && !IsUnder(f, outFull))
            .ToList();
        Parallel.ForEach(others, parallel, f =>
        {
            string dest = Path.Combine(outFull, Path.GetRelativePath(root, f));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(f, dest, true);
        });
    }

    private static bool IsUnder(string file, string? dir)
    {
        if (dir == null)
        {
            return false;
        }

        string prefix = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(file).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static FileResult ProcessFile(string path, string rel, OptimizeOptions options, RunSettings settings)
    {
        byte[] src;
        try
        {
            src = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            return new FileResult { RelativePath = rel, Status = FileStatus.Failed, Note = $"read error: {ex.Message}" };
        }

        if (!VtfParser.TryParse(src, out var info, out string? error) || info == null)
        {
            return new FileResult { RelativePath = rel, Status = FileStatus.Failed, BeforeBytes = src.Length, Note = error };
        }

        FileResult Skipped(string note) => new()
        {
            RelativePath = rel,
            Status = FileStatus.Skipped,
            Note = note,
            BeforeBytes = src.Length,
            AfterBytes = src.Length,
            WidthBefore = info.Width,
            HeightBefore = info.Height,
            WidthAfter = info.Width,
            HeightAfter = info.Height,
            FormatBefore = info.Format,
            FormatAfter = info.Format,
        };

        FilePlan plan;
        try
        {
            plan = Planner.Plan(info, src, rel, options);
        }
        catch (Exception ex)
        {
            return new FileResult { RelativePath = rel, Status = FileStatus.Failed, BeforeBytes = src.Length, Note = $"plan error: {ex.Message}" };
        }

        if (!plan.Apply)
        {
            CopyUnchangedIfNeeded(path, rel, settings);
            return Skipped(plan.Reason ?? "skipped");
        }

        FileResult Optimized(long after, double psnr) => new()
        {
            RelativePath = rel,
            Status = FileStatus.Optimized,
            Note = Describe(plan.Spec!),
            BeforeBytes = src.Length,
            AfterBytes = after,
            WidthBefore = info.Width,
            HeightBefore = info.Height,
            WidthAfter = plan.NewWidth,
            HeightAfter = plan.NewHeight,
            FormatBefore = info.Format,
            FormatAfter = plan.Spec!.TargetFormat,
            Psnr = psnr,
        };

        if (settings.Mode == RunMode.DryRun)
        {
            return Optimized(plan.OutputBytes, double.NaN);
        }

        try
        {
            byte[] dst = VtfTransformer.Apply(src, info, plan.Spec!);
            var verify = Verifier.Verify(src, info, dst, plan);
            if (!verify.Ok)
            {
                CopyUnchangedIfNeeded(path, rel, settings);
                return new FileResult { RelativePath = rel, Status = FileStatus.Failed, BeforeBytes = src.Length, Note = $"verification failed: {verify.Error}" };
            }

            if (verify.Psnr < options.QualityFloorDb)
            {
                CopyUnchangedIfNeeded(path, rel, settings);
                return Skipped($"quality guard {verify.Psnr:F1} dB");
            }

            WriteOutput(path, rel, dst, settings);
            return Optimized(dst.Length, verify.Psnr);
        }
        catch (Exception ex)
        {
            return new FileResult { RelativePath = rel, Status = FileStatus.Failed, BeforeBytes = src.Length, Note = $"{ex.GetType().Name}: {ex.Message}" };
        }
    }

    private static string Describe(VtfTransformSpec spec)
    {
        var parts = new List<string>();
        if (spec.DropMips > 0)
        {
            parts.Add($"drop {spec.DropMips} mip");
        }

        if (spec.DownscaleShift > 0)
        {
            parts.Add($"downscale x{1 << spec.DownscaleShift}");
        }

        parts.Add(spec.TargetFormat.ToString());
        return string.Join(", ", parts);
    }

    private static void CopyUnchangedIfNeeded(string path, string rel, RunSettings settings)
    {
        if (settings.Mode != RunMode.Mirror || !settings.CopyUnchanged || settings.OutputDir == null)
        {
            return;
        }

        string dest = Path.Combine(settings.OutputDir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(path, dest, true);
    }

    private static void WriteOutput(string path, string rel, byte[] dst, RunSettings settings)
    {
        if (settings.Mode == RunMode.Mirror)
        {
            string dest = Path.Combine(settings.OutputDir!, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            AtomicWrite(dest, dst);
            return;
        }

        string backup = Path.Combine(settings.BackupDir!, rel);
        if (File.Exists(backup))
        {
            throw new IOException("backup already exists, refusing to overwrite it");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.Copy(path, backup, false);
        if (new FileInfo(backup).Length != new FileInfo(path).Length)
        {
            throw new IOException("backup size mismatch");
        }

        AtomicWrite(path, dst);
    }

    private static void AtomicWrite(string dest, byte[] data)
    {
        string temp = dest + ".vtfopt.tmp";
        File.WriteAllBytes(temp, data);
        if (File.Exists(dest))
        {
            File.SetAttributes(dest, FileAttributes.Normal);
        }

        File.Move(temp, dest, true);
    }
}
