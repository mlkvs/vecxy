using FlappyBird.Meta;
using Vecxy.Engine;

namespace FlappyBird.Game;

public class Boot : Component
{
    public override void OnAwake()
    {
        var menu = new Menu();
        
        menu.Open();
    }
}