#include "Window.h"

Window::Window(const wchar_t* CLASS_NAME)
{
	WNDCLASS ws = { };

	ws.lpfnWndProc = OnWindowProc;
	ws.hInstance = _instance;
	ws.lpszClassName = CLASS_NAME;

	RegisterClass(&ws);
}

HWND Window::GetHandle() const
{
	return _hwnd;
}

LRESULT Window::OnWindowProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam)
{
	return DefWindowProc(hwnd, uMsg, wParam, lParam);
}
