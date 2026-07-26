using System.Numerics;

namespace Vecxy.Scene;

public interface ISceneGizmoDrawer
{
    void Line(
        Vector3 from,
        Vector3 to,
        Vector4 color,
        float thickness = 1.0f);

    void WireBox(
        Matrix4x4 transform,
        Vector3 size,
        Vector4 color,
        float thickness = 1.0f);

    void WireSphere(
        Vector3 center,
        float radius,
        Vector4 color,
        int segments = 24,
        float thickness = 1.0f);

    void Axes(
        Matrix4x4 transform,
        float size = 1.0f,
        float thickness = 1.0f);
}
