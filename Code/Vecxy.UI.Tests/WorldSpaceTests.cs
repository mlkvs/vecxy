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
        var pointer = new UiSurfacePointer(); var centre = new Vector2(354,180);
        pointer.Update(false,centre,false,false,default,false);
        Check(!pointer.Active,"Looking away must not capture the mouse");
        pointer.Update(true,centre,false,false,default,true);
        Check(pointer.Active && pointer.Position == centre,"Entering the panel starts at the view ray");
        var moved=centre+new Vector2(30,20);
        pointer.Update(true,centre,true,true,moved,true);
        Check(pointer.Position == moved,"Captured pointer follows relative mouse motion");
        pointer.Update(true,centre,true,false,new(720,180),true);
        Check(!pointer.Active && pointer.Released,"Leaving the panel hides the cursor and releases look");
        pointer.Update(true,centre,true,false,new(720,180),false);
        Check(!pointer.Active,"A stationary pointer leaving the edge must not snap back to centre");
        pointer.Update(true,centre,true,true,new(700,180),true);
        Check(pointer.Active && pointer.Position == new Vector2(700,180),"Reversing through the same edge reenters without requiring look-away");
        for (var i=0;i<20;i++)
        {
            pointer.Update(true,centre,true,false,new(720,180),true);
            pointer.Update(true,centre,true,true,new(700,180),true);
            Check(pointer.Active,"Repeated edge exits and reentries must not latch the cursor off");
        }
        pointer.Update(true,centre,true,false,new(720,180),true);
        pointer.Update(false,centre,true,true,new(700,180),true);
        Check(!pointer.Active,"An old released panel must not capture the mouse while looking elsewhere");
        pointer.Update(true,centre,true,false,new(720,180),false);
        Check(pointer.Active && pointer.Position == centre,"Looking away and back also permits reentry");
        pointer.Reset();
        Check(!pointer.Active && !pointer.Released,"Disabling input clears the capture state");
        SurfaceCursorProjectionSurvivesCameraMotion();
    }

    private static void SurfaceCursorProjectionSurvivesCameraMotion()
    {
        var plane=UiWorldPlane.FromBounds(new(-1,-.5f,0),new(1,.5f,0),Matrix4x4.Identity,600);
        var point=new Vector2(870,420);
        var projection=Matrix4x4.CreatePerspectiveFieldOfView(1.3f,1280f/720,.05f,100);
        foreach(var eye in new[]{new Vector3(0,0,3),new Vector3(.4f,.2f,2.8f),new Vector3(-.5f,.3f,2.6f)})
        {
            var vp=Matrix4x4.CreateLookAt(eye,new(.2f,.1f,0),Vector3.UnitY)*projection;
            Check(plane.TryProject(point,vp,1280,720,out var output),"World cursor projects after camera movement");
            Matrix4x4.Invert(vp,out var inverse);
            var clip=new Vector4(output.X/1280*2-1,1-output.Y/720*2,0,1);
            var unprojected=Vector4.Transform(clip,inverse);
            var rayPoint=new Vector3(unprojected.X,unprojected.Y,unprojected.Z)/unprojected.W;
            Check(plane.TryRaycast(eye,Vector3.Normalize(rayPoint-eye),100,true,false,out var actual,out _),"Projected cursor still hits its canvas");
            Check(Vector2.Distance(actual,point)<.01f,"Camera follow must not move the cursor off its original UI point");
        }
        Check(!plane.TryProject(point,Matrix4x4.CreateLookAt(new(0,0,-3),new(0,0,-4),Vector3.UnitY)*projection,1280,720,out _),"A cursor behind the camera cannot retain input capture");
    }
    private static void Near(Vector3 a,Vector3 b)=>Check(Vector3.Distance(a,b)<.002f,"Plane corners preserve source transforms and proportions");
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Invalid(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("Invalid UI plane accepted");}
}
