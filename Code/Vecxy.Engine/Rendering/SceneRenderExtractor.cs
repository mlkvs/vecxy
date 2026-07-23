using Vecxy.Engine.Objects;
using Vecxy.Engine.Scenes;

namespace Vecxy.Engine.Rendering;

internal sealed class SceneRenderExtractor(RenderResourceCache resources)
{
    public RenderWorld Extract(Scene scene, SceneObject? selectedObject)
    {
        var world = new RenderWorld();
        resources.BeginExtraction();
        foreach (var sceneObject in scene.Objects.Where(x => x.IsActive))
        {
            world.Camera ??= sceneObject.GetScript<CameraScript>();
            foreach (var renderer in sceneObject.Scripts.OfType<MeshRenderer>().Where(x => x.IsVisible))
            {
                var mesh = resources.GetMesh(renderer.MeshData);
                renderer.SetPreparedMesh(mesh);
                world.Objects.Add(new RenderObject(sceneObject, renderer, renderer.MeshData, mesh,
                    renderer.Transform.WorldMatrix, mesh.Bounds.Transform(renderer.Transform.WorldMatrix)));
                if (ReferenceEquals(sceneObject, selectedObject)) world.Selected ??= renderer;
            }
        }
        resources.EndExtraction();
        return world;
    }
}
