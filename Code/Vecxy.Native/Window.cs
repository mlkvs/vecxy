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

        [LibraryImport("Vecxy.Native.Windows.dll", EntryPoint = "Window_Initialize", StringMarshalling = StringMarshalling.Utf16)]
        private static partial void Window_Initialize(void* ptr);

        [LibraryImport("Vecxy.Native.Windows.dll", EntryPoint = "Window_Destroy")]
        private static partial void Window_Destroy(void* ptr);

        [LibraryImport("Vecxy.Native.Windows.dll", EntryPoint = "Window_ProcessEvents")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool Window_ProcessEvents(void* ptr);

        [LibraryImport("Vecxy.Native.Windows.dll", EntryPoint = "Window_SwapBuffers")]
        private static partial void Window_SwapBuffers(void* ptr);

        [LibraryImport("Vecxy.Native.Windows.dll", EntryPoint = "Window_GetGLProcAddress", StringMarshalling = StringMarshalling.Utf8)]
        private static partial void* Window_GetGLProcAddress(void* ptr, string procName);

        private void* _wPtr;
        private bool _isDisposed;

        public bool IsRunning { get; private set; }

        public Window(WindowConfig config)
        {
            var className = $"CLASS_{Guid.NewGuid():N}";

            _wPtr = Window_Create(className, config.Title, config.Width, config.Height);

            if (_wPtr == null)
            {
                throw new NotCreatedWindowException();
            }

            IsRunning = true;
        }

        ~Window()
        {
            Dispose();
        }

        public void Initialize()
        {
            Window_Initialize(_wPtr);
        }

        public void ProcessEvents()
        {
            if (_wPtr == null)
            {
                IsRunning = false;
                return;
            }

            IsRunning = Window_ProcessEvents(_wPtr);
        }

        public IntPtr GetProcAddress(string procName, int? slot = null)
        {
            var procPtr = Window_GetGLProcAddress(_wPtr, procName);

            return new IntPtr(procPtr);
        }

        public void SwapBuffers()
        {
            Window_SwapBuffers(_wPtr);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            if (_wPtr == null)
            {
                return;
            }

            IsRunning = false;

            Window_Destroy(_wPtr);

            _wPtr = null;

            _isDisposed = true;

            GC.SuppressFinalize(this);
        }
    }
}
