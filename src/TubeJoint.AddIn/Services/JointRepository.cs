using Inventor;
using TubeJoint.AddIn.Models;

namespace TubeJoint.AddIn.Services;

internal sealed class JointRepository
{
    private const string Prefix = "TubeJoint_";

    public void Save(AssemblyDocument assembly, JointPairRecord record)
    {
        var set = assembly.AttributeSets.Add(Prefix + record.Id, false);
        Add(set, "Schema", 12);
        Add(set, "Id", record.Id);
        Add(set, "MaleOccurrence", record.MaleOccurrenceName);
        Add(set, "MaleDocument", record.MaleDocumentPath);
        Add(set, "FemaleOccurrence", record.FemaleOccurrenceName);
        Add(set, "FemaleDocument", record.FemaleDocumentPath);
        Add(set, "TemplatePath", record.TemplatePath);
        Add(set, "TemplateVersion", record.TemplateVersion);
        Add(set, "PresetName", record.Parameters.PresetName);
        Add(set, "TenonWidthMm", record.Parameters.TenonWidthMm);
        Add(set, "TenonHeightMm", record.Parameters.TenonHeightMm);
        Add(set, "AutoTenonWidth", record.Parameters.AutoTenonWidth ? 1 : 0);
        Add(set, "AutoTenonHeight", record.Parameters.AutoTenonHeight ? 1 : 0);
        Add(set, "SlotTargetThicknessMm", record.Parameters.SlotTargetThicknessMm);
        Add(set, "AutoSlotThickness", record.Parameters.AutoSlotThickness ? 1 : 0);
        Add(set, "ClearanceMm", record.Parameters.ClearanceMm);
        Add(set, "MaleWallMm", record.Parameters.MaleWallMm);
        Add(set, "FemaleWallMm", record.Parameters.FemaleWallMm);
        Add(set, "SideMode", (int)record.Parameters.SideMode);
        Add(set, "WallMask", (int)record.Parameters.WallMask);
        Add(set, "CreateCenterVent", record.Parameters.CreateCenterVent ? 1 : 0);
        Add(set, "InsertionMode", (int)record.Parameters.InsertionMode);
        Add(set, "SideAOffsetMm", record.Parameters.SideAOffsetMm);
        Add(set, "SideBOffsetMm", record.Parameters.SideBOffsetMm);
        Add(set, "JointOffsetXMm", record.Parameters.JointOffsetXMm);
        Add(set, "JointOffsetYMm", record.Parameters.JointOffsetYMm);
        Add(set, "HoleOffsetXMm", record.Parameters.HoleOffsetXMm);
        Add(set, "HoleOffsetYMm", record.Parameters.HoleOffsetYMm);
        Add(set, "ReliefFactorPercent", record.Parameters.ReliefFactorPercent);
        Add(set, "VentScalePercent", record.Parameters.VentScalePercent);
        Add(set, "GeometryStatus", record.GeometryStatus);
        Add(set, "JointPointX", record.JointPointX);
        Add(set, "JointPointY", record.JointPointY);
        Add(set, "JointPointZ", record.JointPointZ);
        Add(set, "MaleAxisX", record.MaleAxisX);
        Add(set, "MaleAxisY", record.MaleAxisY);
        Add(set, "MaleAxisZ", record.MaleAxisZ);
        Add(set, "TenonDirectionX", record.TenonDirectionX);
        Add(set, "TenonDirectionY", record.TenonDirectionY);
        Add(set, "TenonDirectionZ", record.TenonDirectionZ);
        Add(set, "ProfileYAxisX", record.ProfileYAxisX);
        Add(set, "ProfileYAxisY", record.ProfileYAxisY);
        Add(set, "ProfileYAxisZ", record.ProfileYAxisZ);
        Add(set, "InsertionDeviationDegrees", record.InsertionDeviationDegrees);
        Add(set, "SideAGapMm", record.SideAGapMm);
        Add(set, "SideBGapMm", record.SideBGapMm);
    }

    public IReadOnlyList<JointPairRecord> ReadAll(AssemblyDocument assembly)
    {
        var result = new List<JointPairRecord>();
        foreach (AttributeSet set in assembly.AttributeSets)
        {
            if (!set.Name.StartsWith(Prefix, StringComparison.Ordinal))
                continue;

            try
            {
                result.Add(new JointPairRecord
                {
                    Id = ReadString(set, "Id"),
                    MaleOccurrenceName = ReadString(set, "MaleOccurrence"),
                    MaleDocumentPath = ReadString(set, "MaleDocument"),
                    FemaleOccurrenceName = ReadString(set, "FemaleOccurrence"),
                    FemaleDocumentPath = ReadString(set, "FemaleDocument"),
                    TemplatePath = ReadStringOrDefault(set, "TemplatePath"),
                    TemplateVersion = ReadIntOrDefault(set, "TemplateVersion", 1),
                    GeometryStatus = ReadStringOrDefault(set, "GeometryStatus", "Saved"),
                    JointPointX = ReadDoubleOrDefault(set, "JointPointX"),
                    JointPointY = ReadDoubleOrDefault(set, "JointPointY"),
                    JointPointZ = ReadDoubleOrDefault(set, "JointPointZ"),
                    MaleAxisX = ReadDoubleOrDefault(set, "MaleAxisX"),
                    MaleAxisY = ReadDoubleOrDefault(set, "MaleAxisY"),
                    MaleAxisZ = ReadDoubleOrDefault(set, "MaleAxisZ"),
                    TenonDirectionX = ReadDoubleOrDefault(set, "TenonDirectionX"),
                    TenonDirectionY = ReadDoubleOrDefault(set, "TenonDirectionY"),
                    TenonDirectionZ = ReadDoubleOrDefault(set, "TenonDirectionZ"),
                    ProfileYAxisX = ReadDoubleOrDefault(set, "ProfileYAxisX"),
                    ProfileYAxisY = ReadDoubleOrDefault(set, "ProfileYAxisY"),
                    ProfileYAxisZ = ReadDoubleOrDefault(set, "ProfileYAxisZ"),
                    InsertionDeviationDegrees = ReadDoubleOrDefault(set, "InsertionDeviationDegrees"),
                    SideAGapMm = ReadDoubleOrDefault(set, "SideAGapMm"),
                    SideBGapMm = ReadDoubleOrDefault(set, "SideBGapMm"),
                    Parameters = new TubeJointParameters
                    {
                        PresetName = ReadStringOrDefault(set, "PresetName", "Стандартный"),
                        TenonWidthMm = ReadDoubleOrDefault(set, "TenonWidthMm",
                            ReadDoubleOrDefault(set, "TongueDepthMm", 20.0)),
                        TenonHeightMm = ReadDoubleOrDefault(set, "TenonHeightMm", 22.0),
                        AutoTenonWidth = ReadIntOrDefault(set, "AutoTenonWidth", 0) != 0,
                        AutoTenonHeight = ReadIntOrDefault(set, "AutoTenonHeight", 0) != 0,
                        SlotTargetThicknessMm = ReadDoubleOrDefault(set, "SlotTargetThicknessMm",
                            ReadDoubleOrDefault(set, "MaleWallMm", 2.0)),
                        AutoSlotThickness = ReadIntOrDefault(set, "AutoSlotThickness", 0) != 0,
                        ClearanceMm = ReadDouble(set, "ClearanceMm"),
                        MaleWallMm = ReadDouble(set, "MaleWallMm"),
                        FemaleWallMm = ReadDouble(set, "FemaleWallMm"),
                        SideMode = (JointSideMode)ReadIntOrDefault(set, "SideMode", (int)JointSideMode.SideA),
                        WallMask = (JointWallMask)ReadIntOrDefault(set, "WallMask",
                            ReadIntOrDefault(set, "SideMode", (int)JointSideMode.SideA) switch
                            {
                                (int)JointSideMode.SideA => (int)JointWallMask.A,
                                (int)JointSideMode.SideB => (int)JointWallMask.B,
                                _ => (int)JointWallMask.OppositeAB
                            }),
                        CreateCenterVent = ReadIntOrDefault(set, "CreateCenterVent", 0) != 0,
                        InsertionMode = (JointInsertionMode)ReadIntOrDefault(
                            set, "InsertionMode", (int)JointInsertionMode.AlongMaleTubeAxis),
                        SideAOffsetMm = ReadDoubleOrDefault(set, "SideAOffsetMm"),
                        SideBOffsetMm = ReadDoubleOrDefault(set, "SideBOffsetMm"),
                        JointOffsetXMm = ReadDoubleOrDefault(set, "JointOffsetXMm"),
                        JointOffsetYMm = ReadDoubleOrDefault(set, "JointOffsetYMm"),
                        HoleOffsetXMm = ReadDoubleOrDefault(set, "HoleOffsetXMm"),
                        HoleOffsetYMm = ReadDoubleOrDefault(set, "HoleOffsetYMm"),
                        ReliefFactorPercent = ReadDoubleOrDefault(set, "ReliefFactorPercent", 35.0),
                        VentScalePercent = ReadDoubleOrDefault(set, "VentScalePercent", 100.0)
                    }
                });
            }
            catch
            {
                // A damaged or old record must not block the remaining joints.
            }
        }
        return result;
    }

    public int DeleteAll(AssemblyDocument assembly)
    {
        var deleted = 0;
        var sets = assembly.AttributeSets;
        for (var index = sets.Count; index >= 1; index--)
        {
            var set = sets[index];
            if (!set.Name.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            set.Delete();
            deleted++;
        }
        return deleted;
    }

    private static void Add(AttributeSet set, string name, string value) =>
        set.Add(name, ValueTypeEnum.kStringType, value);
    private static void Add(AttributeSet set, string name, double value) =>
        set.Add(name, ValueTypeEnum.kDoubleType, value);
    private static void Add(AttributeSet set, string name, int value) =>
        set.Add(name, ValueTypeEnum.kIntegerType, value);

    private static string ReadString(AttributeSet set, string name) =>
        Convert.ToString(set[name].Value) ?? string.Empty;
    private static double ReadDouble(AttributeSet set, string name) =>
        Convert.ToDouble(set[name].Value);
    private static int ReadInt(AttributeSet set, string name) =>
        Convert.ToInt32(set[name].Value);

    private static double ReadDoubleOrDefault(AttributeSet set, string name, double fallback = 0.0)
    {
        try { return ReadDouble(set, name); } catch { return fallback; }
    }
    private static int ReadIntOrDefault(AttributeSet set, string name, int fallback)
    {
        try { return ReadInt(set, name); } catch { return fallback; }
    }
    private static string ReadStringOrDefault(AttributeSet set, string name, string fallback = "")
    {
        try { return ReadString(set, name); } catch { return fallback; }
    }
}
