#include "Library.h"
#include "Window.h"

EXPORT void* Window_Create(const wchar_t* CLASS_NAME, const wchar_t* TITLE)
{
	try
	{
		const auto window = new Window(CLASS_NAME, TITLE);

		const auto ptr = static_cast<void*>(window);

		return ptr;
	}
	catch (...)
	{
		return nullptr;
	}
}

EXPORT void Window_Destroy(void* handle)
{
	if (!handle)
	{
		return;
	}

	const auto ptr = static_cast<Window*>(handle);

	delete ptr;
}