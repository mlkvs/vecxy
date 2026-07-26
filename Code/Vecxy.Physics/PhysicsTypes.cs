using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Physics;

public enum EPhysicsMotionType : byte
{
    Static,
    Dynamic,
    Kinematic
}

public readonly record struct PhysicsRaycastHit(
    SceneObject SceneObject,
    Collider Collider,
    RigidBody Body,
    Vector3 Point,
    Vector3 Normal,
    float Distance);
