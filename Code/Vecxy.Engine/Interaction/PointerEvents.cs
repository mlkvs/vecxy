using System.Numerics;
using Vecxy.Assets;
using Vecxy.Physics;
using Vecxy.Rendering;
using Vecxy.Input;

namespace Vecxy.Interaction;

public readonly record struct PointerEventData(
    Vector2 ScreenPosition,
    CameraRay Ray,
    PhysicsRaycastHit Hit,
    EMouseButton Button,
    EPointerKind PointerKind = EPointerKind.Mouse,
    int PointerId = 0);

public interface IPointerEnterHandler
{
    void OnPointerEnter(in PointerEventData eventData);
}

public interface IPointerExitHandler
{
    void OnPointerExit(in PointerEventData eventData);
}

public interface IPointerMoveHandler
{
    void OnPointerMove(in PointerEventData eventData);
}

public interface IPointerDownHandler
{
    void OnPointerDown(in PointerEventData eventData);
}

public interface IPointerUpHandler
{
    void OnPointerUp(in PointerEventData eventData);
}

public interface IPointerClickHandler
{
    void OnPointerClick(in PointerEventData eventData);
}
