using Inventor;
using TubeJoint.AddIn.Models;

namespace TubeJoint.AddIn.Services;

internal interface IJointGeometryBuilder
{
    void CreateOrUpdate(
        AssemblyDocument assembly,
        JointPairSelection selection,
        JointPairRecord record);
}
