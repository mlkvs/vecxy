namespace Vecxy.Rendering;

public class D2MainRenderPhaseLayer(ID2RenderContext context) : RenderPhaseLayerBase(context)
{
    public override RENDER_PHASE_LAYER_TYPE Type => RENDER_PHASE_LAYER_TYPE.MAIN_PROCESS;
}