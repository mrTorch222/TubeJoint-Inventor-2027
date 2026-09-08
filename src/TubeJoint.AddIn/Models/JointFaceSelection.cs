using Inventor;

namespace TubeJoint.AddIn.Models;

internal sealed class JointFaceSelection
{
    public JointFaceSelection(ComponentOccurrence occurrence, FaceProxy proxyFace)
    {
        Occurrence = occurrence;
        ProxyFace = proxyFace;
        NativeFace = proxyFace.NativeObject;
    }

    public ComponentOccurrence Occurrence { get; }
    public FaceProxy ProxyFace { get; }
    public Face NativeFace { get; }
}
