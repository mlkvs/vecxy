using Vecxy.Scene;

namespace Vecxy.Physics;

public abstract class Collider : AComponent
{
    public bool IsTrigger
    {
        get => _isTrigger;
        set
        {
            if (_isTrigger == value)
                return;

            _isTrigger = value;
            NotifyChanged();
        }
    }
    
    private bool _isTrigger;
}
