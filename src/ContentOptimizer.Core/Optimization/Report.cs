using ContentOptimizer.Core.Vtf;

namespace ContentOptimizer.Core.Optimization;

public enum FileStatus
{
    Optimized,
    Skipped,
    Failed,
}

public enum RunMode
{
    DryRun,
    Mirror,
    InPlace,
}

public sealed class RunSettings
{
    public RunMode Mode { get; set; } = RunMode.DryRun;
    public string? OutputDir { get; set; }
    public string? BackupDir { get; set; }
    public bool CopyUnchanged { get; set; }
    public int Threads { get; set; } = Environment.ProcessorCount;
}

public sealed class FileResult
{
    public string RelativePath { get; init; } = "";
    public FileStatus Status { get; init; }
    public string? Note { get; init; }
    public long BeforeBytes { get; init; }
    public long AfterBytes { get; init; }
    public int WidthBefore { get; init; }
    public int HeightBefore { get; init; }
    public int WidthAfter { get; init; }
    public int HeightAfter { get; init; }
    public ImageFormat? FormatBefore { get; init; }
    public ImageFormat? FormatAfter { get; init; }
    public double Psnr { get; init; }

    public long Saved => Status == FileStatus.Optimized ? BeforeBytes - AfterBytes : 0;
}

public sealed class RunReport
{
    public RunMode Mode { get; init; }
    public TimeSpan Elapsed { get; init; }
    public IReadOnlyList<FileResult> Files { get; init; } = Array.Empty<FileResult>();

    public int Count(FileStatus status) => Files.Count(f => f.Status == status);
    public long TotalBefore => Files.Sum(f => f.BeforeBytes);
    public long TotalAfter => Files.Sum(f => f.Status == FileStatus.Optimized ? f.AfterBytes : f.BeforeBytes);
    public long TotalSaved => TotalBefore - TotalAfter;
}

public readonly record struct RunProgress(int Done, int Total, string CurrentFile);

public sealed record EstimateResult(long BeforeBytes, long[] AfterBytes);
