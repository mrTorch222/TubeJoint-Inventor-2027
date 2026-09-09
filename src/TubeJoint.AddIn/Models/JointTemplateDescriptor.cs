namespace TubeJoint.AddIn.Models;

/// <summary>COM-free description of one editable IPT profile variant.</summary>
internal sealed record JointTemplateDescriptor(
    string Id,
    string DisplayName,
    string FileName,
    string FullPath)
{
    public override string ToString() => $"Эскиз · {DisplayName}";
}
