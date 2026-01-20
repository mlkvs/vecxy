#include "Window.h"
#include <stdexcept>

Window::Window(const wchar_t* CLASS_NAME)
{
	WNDCLASS ws = { };

	ws.lpfnWndProc = OnWindowProc;
	ws.hInstance = _instance;
	ws.lpszClassName = CLASS_NAME;

	RegisterClass(&ws);

	_hwnd = CreateWindowEx
	(
		0,
		CLASS_NAME,
		L"Vecxy Native Window",
		WS_OVERLAPPEDWINDOW,
		CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT,
		nullptr,
		nullptr,
		_instance,
		nullptr
	);

	if (_hwnd == nullptr)
	{
		throw std::runtime_error("Failed to create window.");
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
	return DefWindowProc(hwnd, uMsg, wParam, lParam);
}
