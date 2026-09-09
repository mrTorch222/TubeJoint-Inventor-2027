namespace TubeJoint.AddIn.UI;

/// <summary>
/// COM-free presentation snapshot used by the tube preparation dialog.
/// Inventor documents and geometry never cross the UI boundary.
/// </summary>
internal sealed record TubePreparationViewRow
{
    public required string DocumentKey { get; init; }
    public required string CurrentFileName { get; init; }
    public required string MeasuredSection { get; init; }
    public required string NominalSection { get; init; }
    public required string WallThickness { get; init; }
    public required int Quantity { get; init; }
    public required string ProposedFileName { get; init; }
    public required string Status { get; init; }
    public required bool CanPrepare { get; init; }
    public required bool CanRename { get; init; }
}
