using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Physics;

public interface IPhysicsSystem
{
    PhysicsSettings Settings { get; }

    void AddForce(RigidBody body, Vector3 force);
    void AddImpulse(RigidBody body, Vector3 impulse);
    void Teleport(
        RigidBody body,
        Vector3 position,
        Quaternion rotation);

    bool Raycast(
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        SceneObject? ignoreSceneObject,
        out PhysicsRaycastHit hit);
}
