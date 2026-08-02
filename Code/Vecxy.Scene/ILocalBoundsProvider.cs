using System.Numerics;

namespace Vecxy.Scene;

/// <summary>
/// Supplies geometry bounds in the owning object's local coordinate space.
/// </summary>
public interface ILocalBoundsProvider
{
    Vector3 LocalBoundsMin { get; }
    Vector3 LocalBoundsMax { get; }

    Vector3 LocalBoundsSize =>
        LocalBoundsMax - LocalBoundsMin;

    Vector3 LocalBoundsCenter =>
        (LocalBoundsMin + LocalBoundsMax) * 0.5f;
}
