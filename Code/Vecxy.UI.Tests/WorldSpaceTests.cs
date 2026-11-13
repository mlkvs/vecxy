using System.Numerics;
using Vecxy.UI;

internal static class WorldSpaceTests
{
    public static void Run()
    {
        var min = new Vector3(-.6f,-.2f,0); var max = new Vector3(.6f,.2f,0);
        var model = Matrix4x4.CreateScale(2,3,1) * Matrix4x4.CreateRotationY(.47f) * Matrix4x4.CreateTranslation(4,2,-5);
        var plane = UiWorldPlane.FromBounds(min,max,model,500);
        Check(Math.Abs(plane.Width-1200)<=1 && Math.Abs(plane.Height-600)<=1,"Canvas resolution follows transformed plane dimensions");
        Near(Vector3.Transform(Vector3.Zero,plane.CanvasToWorld),Vector3.Transform(new(-.6f,.2f,0),model));
        Near(Vector3.Transform(new(plane.Width,plane.Height,0),plane.CanvasToWorld),Vector3.Transform(new(.6f,-.2f,0),model));
        var normal = Vector3.Normalize(new Vector3(plane.CanvasToWorld.M31,plane.CanvasToWorld.M32,plane.CanvasToWorld.M33));
        Check(plane.TryRaycast(plane.Center+normal*3,-normal,10,true,false,out var point,out var distance),"Ray hits front of transformed plane");
        Check(Vector2.Distance(point,new(plane.Width*.5f,plane.Height*.5f))<.002f && Math.Abs(distance-3)<.001f,"Pointer maps to the exact canvas centre");
        Check(!plane.TryRaycast(plane.Center-normal*3,normal,10,true,false,out _,out _),"Back face does not capture input");
        Check(plane.TryRaycast(plane.Center-normal*3,normal,10,true,true,out _,out _),"Double-sided canvas supports back-face input");
        Check(!plane.TryRaycast(plane.Center+normal*3,-normal,2,true,false,out _,out _),"Far plane limits hit testing");
        var outside=Vector3.Transform(new(plane.Width+20,plane.Height*.5f,0),plane.CanvasToWorld);
        Check(!plane.TryRaycast(outside+normal,-normal,10,true,false,out _,out _),"Outside area does not hit the canvas");
        Check(plane.TryRaycast(outside+normal,-normal,10,false,false,out var captured,out _) && captured.X>plane.Width,"Captured drags can continue beyond canvas bounds");
        Check(!plane.TryRaycast(plane.Center+normal,Vector3.UnitY,10,true,false,out _,out _),"Parallel ray is rejected");
        var floor=UiWorldPlane.FromBounds(new(-1,0,-.5f),new(1,0,.5f),Matrix4x4.Identity,100);
        Check(floor.TryRaycast(new(0,3,0),-Vector3.UnitY,10,true,false,out _,out _),"XZ plane uses its upward face");
        Invalid(()=>UiWorldPlane.FromBounds(-Vector3.One,Vector3.One,Matrix4x4.Identity,100));
        Invalid(()=>UiWorldPlane.FromBounds(min,max,Matrix4x4.CreateScale(0),100));
        SurfaceCursorReleasesLookAtTheBoundary();
        Console.WriteLine("WorldSpace UI projection, dimensions, input, clipping and invalid-area checks passed.");
    }
    private static void SurfaceCursorReleasesLookAtTheBoundary()
    {
        var pointer = new UiSurfacePointer(); var centre = new Vector2(640,360);
        pointer.Update(false,false,centre,Vector2.Zero);
        Check(!pointer.Active,"Looking away must not capture the mouse");
        pointer.Update(true,false,centre,new(30,20));
        Check(pointer.Active && pointer.Output == centre,"Entering the panel starts at the view ray");
        pointer.Update(true,true,centre,new(30,20));
        Check(pointer.Output == centre+new Vector2(30,20),"Captured pointer follows relative mouse motion");
        pointer.Update(true,false,centre,new(2000,0));
        Check(!pointer.Active && pointer.NeedsLookAway,"Leaving the panel hides the cursor and releases look");
        pointer.Update(true,true,centre,Vector2.Zero);
        Check(!pointer.Active,"A pointer leaving the edge must not immediately snap back to centre");
        pointer.Update(false,false,centre,Vector2.Zero);
        pointer.Update(true,true,centre,Vector2.Zero);
        Check(pointer.Active && pointer.Output == centre,"Looking away and back permits reentry");
        pointer.Reset();
        Check(!pointer.Active && !pointer.NeedsLookAway,"Disabling input clears the capture state");
    }
    private static void Near(Vector3 a,Vector3 b)=>Check(Vector3.Distance(a,b)<.002f,"Plane corners preserve source transforms and proportions");
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Invalid(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("Invalid UI plane accepted");}
}
