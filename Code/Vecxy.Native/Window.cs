using System.Drawing;
using System.Runtime.InteropServices;

namespace Vecxy.Native
{
    public struct WindowConfig
    {
        public string Title;
        public int Width;
        public int Height;
    }

    public class NotCreatedWindowException() : Exception("Could not created native window.");

    public unsafe partial class Window : IDisposable
    {
        [LibraryImport("Vecxy.Native.Windows.dll", EntryPoint = "Window_Create", StringMarshalling = StringMarshalling.Utf16)]
        private static partial void* Window_Create(string className, string title, int width, int height);

        [LibraryImport("Vecxy.Native.Windows.dll", EntryPoint = "Window_Destroy")]
        private static partial void Window_Destroy(void* ptr);

        [LibraryImport("Vecxy.Native.Windows.dll", EntryPoint = "Window_ProcessEvents")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool Window_ProcessEvents(void* ptr);

        private void* _ptr;
        private bool _isDisposed;

        public bool IsRunning { get; private set; }

        public Window(WindowConfig config)
        {
            var className = $"CLASS_{Guid.NewGuid():N}";

            _ptr = Window_Create(className, config.Title, config.Width, config.Height);

            if (_ptr == null)
            {
                throw new NotCreatedWindowException();
            }

            IsRunning = true;
        }

        ~Window()
        {
            Dispose();
        }

        public void ProcessEvents()
        {
            if (_ptr == null)
            {
                IsRunning = false;
                return;
            }

            IsRunning = Window_ProcessEvents(_ptr);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            if (_ptr == null)
            {
                return;
            }

            IsRunning = false;

            Window_Destroy(_ptr);

            _ptr = null;

            _isDisposed = true;

            GC.SuppressFinalize(this);
        }
    }
}
