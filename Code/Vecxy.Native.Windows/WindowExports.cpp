#include "Library.h"
#include "Window.h"

EXPORT void* Window_Create(const wchar_t* className, const wchar_t* title, const int width, const int height)
{
	try
	{
		const auto window = new Window(className, title, width, height);

		const auto ptr = static_cast<void*>(window);

		return ptr;
	}
	catch (...)
	{
		return nullptr;
	}
}

EXPORT void Window_Destroy(void* ptr)
{
	if (!ptr)
	{
		return;
	}

	const auto pWnd = static_cast<Window*>(ptr);

	delete pWnd;
}

EXPORT bool Window_ProcessEvents(void* ptr)
{
	if(ptr == nullptr)
	{
		return false;
	}

	const auto pWnd = static_cast<Window*>(ptr);

	return pWnd->ProcessEvents();
}
