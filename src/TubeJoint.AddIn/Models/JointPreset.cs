namespace TubeJoint.AddIn.Models;

internal enum JointPresetSortOrder
{
    RecentlyUsed,
    Created,
    Alphabetical,
    Modified
}

internal enum JointPresetStartupMode
{
    LastUsed,
    SpecificPreset,
    NoPreset
}

/// <summary>
/// User-owned reusable options. Measured wall values and per-joint placement
/// offsets are deliberately excluded because they belong to the current assembly.
/// </summary>
internal sealed record JointPreset
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = string.Empty;
    public string TemplateFileName { get; init; } = string.Empty;
    public double TenonWidthMm { get; init; }
    public double TenonHeightMm { get; init; }
    public bool AutoTenonWidth { get; init; }
    public bool AutoTenonHeight { get; init; }
    public double ClearanceMm { get; init; }
    public JointSideMode SideMode { get; init; } = JointSideMode.Both;
    public bool CreateCenterVent { get; init; } = true;
    public double ReliefFactorPercent { get; init; } = 35.0;
    public double VentScalePercent { get; init; } = 100.0;
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUsedUtc { get; init; } = DateTimeOffset.UtcNow;
}

internal sealed class JointPresetCatalog
{
    public const int CurrentSchema = 1;
    public int Schema { get; set; } = CurrentSchema;
    public JointPresetSortOrder SortOrder { get; set; } = JointPresetSortOrder.RecentlyUsed;
    public JointPresetStartupMode StartupMode { get; set; } = JointPresetStartupMode.NoPreset;
    public string? DefaultPresetId { get; set; }
    public string? LastUsedPresetId { get; set; }
    public List<JointPreset> Presets { get; set; } = new();
}
