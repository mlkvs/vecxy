using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Editor;

public interface IEditorGizmos
{
    void Register(Action<IEditorGizmoDrawer> draw);
    void Unregister(Action<IEditorGizmoDrawer> draw);
}

public interface IEditorGizmoDrawer : ISceneGizmoDrawer
{
}
