#include "Window.h"
#include <stdexcept>

Window::Window(const wchar_t* CLASS_NAME, const wchar_t* TITLE)
{
	WNDCLASS ws = { };

	if(GetClassInfoW(_instance, CLASS_NAME, &ws) == TRUE)
	{
		throw std::runtime_error("Failed to create window. Already created.");
	}

	ws.lpfnWndProc = OnWindowProc;
	ws.hInstance = _instance;
	ws.lpszClassName = CLASS_NAME;

	RegisterClass(&ws);

	_hwnd = CreateWindowExW
	(
		0,
		CLASS_NAME,
		TITLE,
		WS_OVERLAPPEDWINDOW,
		CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT,
		nullptr,
		nullptr,
		_instance,
		nullptr
	);

	if(_hwnd == nullptr)
	{
		throw std::runtime_error("Failed to create window. Hwnd null.");
	}

	ShowWindow(_hwnd, true);

	MSG msg = { };

	while(GetMessageW(&msg, nullptr, 0, 0) > 0)
	{
		TranslateMessage(&msg);
		DispatchMessageW(&msg);
	}
}

Window::~Window()
{
}

HWND Window::GetHandle() const
{
	return _hwnd;
}

LRESULT Window::OnWindowProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam)
{
	switch(uMsg)
	{
		case WM_SIZE:
		{
			const int width = LOWORD(lParam);
			const int height = HIWORD(lParam);

			OnSize(hwnd, static_cast<UINT>(wParam), width, height);

			break;
		}


	}

	return DefWindowProcW(hwnd, uMsg, wParam, lParam);
}

void Window::OnSize(HWND hwnd, UINT flag, int width, int height)
{

}
