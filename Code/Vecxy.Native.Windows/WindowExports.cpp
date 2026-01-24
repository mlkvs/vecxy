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

EXPORT void Window_Initialize(void* ptr)
{
	if(ptr == nullptr)
	{
		return;
	}

	const auto pWnd = static_cast<Window*>(ptr);

	pWnd->Initialize();
}

EXPORT void Window_Destroy(void* ptr)
{
	if (ptr == nullptr)
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

EXPORT void Window_SwapBuffers(void* ptr)
{
	if(ptr == nullptr)
	{
		return;
	}

	const auto pWnd = static_cast<Window*>(ptr);

	pWnd->SwapBuffers();
}

EXPORT void* Window_GetGLProcAddress(void* ptr, const char* procName)
{
	if(ptr == nullptr)
	{
		return nullptr;
	}

	const auto pWnd = static_cast<Window*>(ptr);

	const auto procPtr = pWnd->GetProcAddress(procName);

	return procPtr;
}