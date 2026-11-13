using System.Numerics;

namespace Vecxy.UI;

public enum EUiWorldInputMode { Mouse, ViewRay, SurfaceCursor }

public readonly record struct UiRaycastHit(UiCanvas Canvas, UiDocument Document, UiElement? Element, Vector2 Position, float Distance);

public static class UiWorldInteraction
{
    public static bool RaycastWorld(this IUiManager ui, Vector3 origin, Vector3 direction, out UiRaycastHit hit, float maxDistance = 1000)
    {
        hit = default; var found = false; var nearest = maxDistance;
        foreach (var document in ui.Documents)
            if (document.Canvas is { } canvas && canvas.Raycast(origin,direction,out var candidate,nearest) && candidate.Element is not null)
            { hit = candidate; nearest = candidate.Distance; found = true; }
        return found;
    }

    /// <summary>Route an explicit ray activation through the normal UI focus/pointer/click events.</summary>
    public static bool ActivateWorld(this IUiManager ui, UiRaycastHit hit)
    {
        if (hit.Element is not { } element || !hit.Canvas.IsActive || !hit.Canvas.Interactive || hit.Canvas.RenderMode != EUiRenderMode.WorldSpace || !hit.Document.CanRender ||
            !ui.Documents.Contains(hit.Document) || hit.Canvas.Document != hit.Document ||
            hit.Document.Root.HitTest(hit.Position) != element) return false;
        ui.Focus(element);
        element.RaisePointerPressed(new UiPointerEvent(hit.Position,Vector2.Zero));
        element.IsActive = true;
        element.RaisePointerReleased(new UiPointerEvent(hit.Position,Vector2.Zero));
        element.IsActive = false;
        element.RaiseClicked(hit.Position);
        return true;
    }
}

internal sealed class UiSurfacePointer
{
    public bool Active { get; private set; }
    public bool Released { get; private set; }
    public Vector2 Position { get; private set; }
    private bool _lookedAway;

    public void Reset() { Active = Released = _lookedAway = false; Position = default; }

    // Position belongs to the canvas, so moving the camera cannot drag the cursor.
    public void Update(bool centreInside, Vector2 centre, bool candidateValid, bool candidateInside, Vector2 candidate, bool moved)
    {
        if (Active)
        {
            if (candidateValid) Position = candidate;
            if (!candidateInside) { Active = false; Released = true; _lookedAway = !centreInside; }
            return;
        }
        if (!centreInside) _lookedAway = true;
        if (centreInside && (!Released || _lookedAway))
            Enter(centre);
        else if (centreInside && Released && moved && candidateInside)
            Enter(candidate);
    }

    private void Enter(Vector2 position)
    {
        Position = position; Active = true; Released = _lookedAway = false;
    }
}
