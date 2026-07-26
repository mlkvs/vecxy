using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Physics;

public interface IPhysicsSystem
{
    Vector3 Gravity { get; set; }

    bool Raycast(
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        SceneObject? ignoreSceneObject,
        out PhysicsRaycastHit hit);
}
