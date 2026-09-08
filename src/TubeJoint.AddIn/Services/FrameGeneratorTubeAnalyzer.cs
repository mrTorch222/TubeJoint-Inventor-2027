using Inventor;
using TubeJoint.AddIn.Models;

namespace TubeJoint.AddIn.Services;

internal sealed class FrameGeneratorTubeAnalyzer : ITubeMemberAnalyzer
{
    private const string UserPropertiesId = "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}";
    private readonly GenericTubeAnalyzer _geometry;

    public FrameGeneratorTubeAnalyzer(Inventor.Application application) =>
        _geometry = new GenericTubeAnalyzer(application, TubeMemberSource.FrameGenerator);

    public bool CanAnalyze(ComponentOccurrence occurrence)
    {
        if (occurrence.DefinitionDocumentType != DocumentTypeEnum.kPartDocumentObject)
            return false;
        return IsFrameMember((PartDocument)((PartComponentDefinition)occurrence.Definition).Document);
    }

    public TubeMemberAnalysis Analyze(ComponentOccurrence occurrence) => _geometry.Analyze(occurrence);

    public static bool IsFrameMember(PartDocument document) =>
        HasProperty(document, "G_L") || HasProperty(document, "G_W") || HasProperty(document, "G_H");

    private static bool HasProperty(PartDocument document, string name)
    {
        try
        {
            var set = document.PropertySets[UserPropertiesId];
            _ = set[name];
            return true;
        }
        catch
        {
            return false;
        }
    }
}
