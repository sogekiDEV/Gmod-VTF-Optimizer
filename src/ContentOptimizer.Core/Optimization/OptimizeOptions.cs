namespace ContentOptimizer.Core.Optimization;

public sealed class OptimizeOptions
{
    public static readonly string[] DefaultExcludes =
    {
        "**/materials/vgui/**",
        "**/materials/hud/**",
        "**/materials/gui/**",
        "**/materials/console/**",
        "**/materials/spawnicons/**",
    };

    private PathGlob[] excludeGlobs = Array.Empty<PathGlob>();
    private PathGlob[] includeGlobs = Array.Empty<PathGlob>();
    private string[] excludes = Array.Empty<string>();
    private string[] includes = Array.Empty<string>();

    public int MaxSize { get; set; } = 1024;
    public int TriggerSide { get; set; }
    public bool ConvertFormats { get; set; } = true;
    public int MinConvertSide { get; set; } = 64;
    public bool ConvertNormalMaps { get; set; } = true;
    public bool DetectOpaque { get; set; } = true;
    public bool RespectNoLod { get; set; } = true;
    public double QualityFloorDb { get; set; } = 27.0;

    public string[] Excludes
    {
        get => excludes;
        set
        {
            excludes = value;
            excludeGlobs = value.Select(p => new PathGlob(p)).ToArray();
        }
    }

    public string[] Includes
    {
        get => includes;
        set
        {
            includes = value;
            includeGlobs = value.Select(p => new PathGlob(p)).ToArray();
        }
    }

    public OptimizeOptions()
    {
        Excludes = DefaultExcludes;
    }

    public bool IsExcluded(string relativePath)
    {
        if (includeGlobs.Length > 0 && !includeGlobs.Any(g => g.IsMatch(relativePath)))
        {
            return true;
        }

        return excludeGlobs.Any(g => g.IsMatch(relativePath));
    }
}
