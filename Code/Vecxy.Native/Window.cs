using System.Runtime.InteropServices;

namespace Vecxy.Native
{
    public class WindowSettings
    {
        public string Title { get; set; }
    }

    public unsafe partial class Window : IDisposable
    {
        [LibraryImport("Vecxy.Native.Windows.dll", EntryPoint = "Window_Create", StringMarshalling = StringMarshalling.Utf16)]
        private static partial void* Window_Create(string id, string title);

        [LibraryImport("Vecxy.Native.Windows.dll", EntryPoint = "Window_Destroy")]
        private static partial void Window_Destroy(void* ptr);

        private readonly Guid _id;
        private readonly WindowSettings _settings;

        private void* _ptr;

        public Window(WindowSettings settings)
        {
            _id = Guid.NewGuid();
            _settings = settings;

            _ptr = Window_Create(_id.ToString(), settings.Title);
        }

        ~Window()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (_ptr == null)
            {
                return;
            }

            Window_Destroy(_ptr);

            _ptr = null;
        }
    }
}
