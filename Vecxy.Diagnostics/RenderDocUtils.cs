using Evergine.Bindings.RenderDoc;

namespace Vecxy.Diagnostics
{
    public static class RenderDocUtils
    {
        private static RenderDoc _renderDoc;
        private static bool _loaded = false;

        public static void Load()
        {
            try
            {
                RenderDoc.Load(out _renderDoc);
                _loaded = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to load RenderDoc: " + ex.Message);
            }
        }

        public static void StartFrameCapture(IntPtr wndHandle)
        {
            if (!_loaded) return;

            try
            {
                _renderDoc.API.StartFrameCapture(IntPtr.Zero, wndHandle);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception during StartFrameCapture: " + ex);
            }
        }

        public static void EndFrameCapture(IntPtr wndHandle)
        {
            if (!_loaded) return;

            try
            {
                _renderDoc.API.EndFrameCapture(IntPtr.Zero, wndHandle);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception during EndFrameCapture: " + ex);
            }
        }
    }
}