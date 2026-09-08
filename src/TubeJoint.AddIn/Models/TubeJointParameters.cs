namespace TubeJoint.AddIn.Models;

internal enum JointSideMode
{
    SideA = 0,
    SideB = 1,
    Both = 2
}

internal enum JointWallPair
{
    AB = 0,
    CD = 1
}

[Flags]
internal enum JointWallMask
{
    None = 0,
    A = 1,
    B = 2,
    C = 4,
    D = 8,
    OppositeAB = A | B,
    All = A | B | C | D
}

// Reserved now so angled joints can choose their physical assembly direction
// without changing the saved-record format later.
internal enum JointInsertionMode
{
    AlongMaleTubeAxis = 0,
    AlongFemaleFaceNormal = 1
}

internal sealed class TubeJointParameters
{
    public string PresetName { get; set; } = "Стандартный";
    public double TenonWidthMm { get; set; } = 20.0;
    public double TenonHeightMm { get; set; } = 22.0;
    public bool AutoTenonWidth { get; set; }
    public bool AutoTenonHeight { get; set; }
    public double SlotTargetThicknessMm { get; set; } = 2.0;
    public bool AutoSlotThickness { get; set; }
    public double ClearanceMm { get; set; } = 0.20;
    public double MaleWallMm { get; set; } = 2.0;
    public double FemaleWallMm { get; set; } = 2.0;
    public JointSideMode SideMode { get; set; } = JointSideMode.Both;
    public JointWallMask WallMask { get; set; } = JointWallMask.OppositeAB;
    public bool CreateCenterVent { get; set; } = true;
    public JointInsertionMode InsertionMode { get; set; } = JointInsertionMode.AlongFemaleFaceNormal;

    // Persistent placement parameters for the next UI step: 3D drag handles
    // will modify these values, while preview and final features already use them.
    public double SideAOffsetMm { get; set; }
    public double SideBOffsetMm { get; set; }

    // Common in-plane placement is shared by tenons, slots and the hole.
    // These values are the stable data contract for Inventor triad manipulators.
    public double JointOffsetXMm { get; set; }
    public double JointOffsetYMm { get; set; }
    public double HoleOffsetXMm { get; set; }
    public double HoleOffsetYMm { get; set; }
    // One UI percentage drives both template relief sizes through TJ_ReliefFactor.
    // The actual reliefs remain dependent on material thickness, not profile scale.
    public double ReliefFactorPercent { get; set; } = 35.0;
    public double VentScalePercent { get; set; } = 100.0;
}
