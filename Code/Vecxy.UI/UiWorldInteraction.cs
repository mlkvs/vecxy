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
    public bool NeedsLookAway { get; private set; }
    public Vector2 Output { get; private set; }
    public void Reset() { Active = NeedsLookAway = false; Output = default; }
    public void Update(bool centreInside, bool movedInside, Vector2 centre, Vector2 delta)
    {
        if (NeedsLookAway) { if (!centreInside) NeedsLookAway = false; return; }
        if (!Active) { if (centreInside) { Active = true; Output = centre; } return; }
        Output += delta;
        if (!movedInside) { Active = false; NeedsLookAway = true; }
    }
}
