using System.Numerics;
using Vecxy.Prototypes;
using Vecxy.Scene;

namespace Vecxy.AI;

[SingleComponent]
[Authoring("Navigation Obstacle", Category = "AI", Key = "navigationObstacle",
    Description = "Updates the walkable area while this object moves.")]
public sealed class NavigationObstacle : AComponent
{
    public bool UseColliders { get; set; } = true;
    public Vector3 Center { get; set; }
    public Vector3 Size { get; set; } = Vector3.One;

    public sealed class Prototype : APrototype<NavigationObstacle, Prototype.Options>
    {
        public sealed class Options
        {
            [Authoring("Use Object Colliders", Order = 0,
                Description = "Follow the object and child colliders. Turn this off to edit a navigation-only box.")]
            public bool UseColliders { get; set; } = true;

            [Authoring("Box Offset", Unit = "m", Order = 1,
                Description = "Moves the custom box relative to the object origin.")]
            public Vector3 Center { get; set; }

            [Authoring("Box Dimensions", Unit = "m", Min = .01, Order = 2,
                Description = "Width, depth and height of the custom navigation-only box in local axes.")]
            public Vector3 Size { get; set; } = Vector3.One;
        }

        protected override NavigationObstacle Instantiate(IPrototypeContext context)
        {
            var scene = RequireContext<ScenePrototypeContext>(context);
            return (scene.Object ?? scene.CreateObject("Navigation obstacle")).AddComponent<NavigationObstacle>();
        }

        protected override void Configure(NavigationObstacle obstacle, Options options)
        {
            obstacle.UseColliders = options.UseColliders;
            obstacle.Center = options.Center;
            obstacle.Size = options.Size;
        }
    }
}

/// <summary>Navigation-only volume. Its transform controls the size of the unit box.</summary>
[SingleComponent]
public sealed class NavigationBlocker : AComponent;
