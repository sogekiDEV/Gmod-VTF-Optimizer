namespace ContentOptimizer.Core.Vtf;

public static class VtfFlags
{
    public const uint PointSample = 0x1;
    public const uint Trilinear = 0x2;
    public const uint ClampS = 0x4;
    public const uint ClampT = 0x8;
    public const uint Anisotropic = 0x10;
    public const uint HintDxt5 = 0x20;
    public const uint Srgb = 0x40;
    public const uint Normal = 0x80;
    public const uint NoMip = 0x100;
    public const uint NoLod = 0x200;
    public const uint MinMip = 0x400;
    public const uint Procedural = 0x800;
    public const uint OneBitAlpha = 0x1000;
    public const uint EightBitAlpha = 0x2000;
    public const uint EnvMap = 0x4000;
    public const uint RenderTarget = 0x8000;
    public const uint DepthRenderTarget = 0x10000;
    public const uint SingleCopy = 0x40000;
    public const uint SsBump = 0x8000000;
}
