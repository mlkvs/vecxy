using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Physics;

public enum EPhysicsMotionType : byte
{
    Dynamic,
    Kinematic
}

public readonly record struct PhysicsRaycastHit(
    SceneObject SceneObject,
    Collider Collider,
    RigidBody? Body,
    Vector3 Point,
    Vector3 Normal,
    float Distance);

public readonly record struct PhysicsContact(
    SceneObject SelfObject,
    Collider SelfCollider,
    SceneObject OtherObject,
    Collider OtherCollider,
    Vector3 Point,
    Vector3 Normal,
    float Penetration);

public interface ICollisionHandler
{
    void OnCollisionEnter(in PhysicsContact contact);
    void OnCollisionStay(in PhysicsContact contact);
    void OnCollisionExit(in PhysicsContact contact);
}

public interface ITriggerHandler
{
    void OnTriggerEnter(in PhysicsContact contact);
    void OnTriggerStay(in PhysicsContact contact);
    void OnTriggerExit(in PhysicsContact contact);
}
