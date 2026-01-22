#include <stdexcept>

#include "Library.h"
#include "Window.h"

Window::Window(const wchar_t* className, const wchar_t* title, const int width, const int height) : _className(className)
{
	WNDCLASSEXW ws = { sizeof(WNDCLASSEXW) };

	if(GetClassInfoExW(LIB_INSTANCE, _className.c_str(), &ws) == TRUE)
	{
		throw std::runtime_error("Failed to create window. Already created.");
	}

	ws.lpfnWndProc = OnStaticWndProc;
	ws.hInstance = LIB_INSTANCE;
	ws.lpszClassName = _className.c_str();

	RegisterClassExW(&ws);

	RECT rect = { 0, 0, width, height };
	AdjustWindowRect(&rect, WS_OVERLAPPEDWINDOW, FALSE);

	_hwnd = CreateWindowExW
	(
		0,
		_className.c_str(),
		title,
		WS_OVERLAPPEDWINDOW,
		CW_USEDEFAULT, 
		CW_USEDEFAULT, 
		rect.right - rect.left,
		rect.bottom - rect.top,
		nullptr,
		nullptr,
		LIB_INSTANCE,
		this
	);

	if(_hwnd == nullptr)
	{
		throw std::runtime_error("Failed to create window. Hwnd null.");
	}

	ShowWindow(_hwnd, SW_SHOW);
}

Window::~Window()
{
	if (_hwnd != nullptr)
	{
		DestroyWindow(_hwnd);
	}

	UnregisterClassW(_className.c_str(), LIB_INSTANCE);
}

HWND Window::GetHandle() const
{
	return _hwnd;
}

bool Window::ProcessEvents()
{
	MSG msg = { };

	while(PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE))
	{
		if(msg.message == WM_QUIT) 
		{
			return false;
		}

		TranslateMessage(&msg);
		DispatchMessageW(&msg);
	}

	return true;
}

LRESULT Window::OnStaticWndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
{
	Window* pWindow = nullptr;

	if (msg == WM_NCCREATE)
	{
		const LPVOID lpCreateParams = reinterpret_cast<CREATESTRUCT*>(lp)->lpCreateParams;

		pWindow = static_cast<Window*>(lpCreateParams);

		const LONG_PTR lPtr = reinterpret_cast<LONG_PTR>(pWindow);

		SetWindowLongPtrW(hwnd, GWLP_USERDATA, lPtr);
	}
	else
	{
		const LONG_PTR lPtr = GetWindowLongPtrW(hwnd, GWLP_USERDATA);
		pWindow = reinterpret_cast<Window*>(lPtr);
	}

	if (pWindow)
	{
		return pWindow->OnWndProc(hwnd, msg, wp, lp);
	}

	return DefWindowProcW(hwnd, msg, wp, lp);
}

LRESULT Window::OnWndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
{
	switch(msg)
	{
		case WM_DESTROY:
		{
			PostQuitMessage(0);

			return 0;
		}

		case WM_SIZE:
		{
			const int width = LOWORD(lp);
			const int height = HIWORD(lp);

			OnSize(hwnd, static_cast<UINT>(wp), width, height);

			break;
		}

		/*case WM_NCHITTEST: {
			LRESULT hit = DefWindowProc(hwnd, msg, wp, lp);
			if(hit == HTCLIENT) return HTCAPTION;
			return hit;
		}*/

		case WM_PAINT:
		{
			PAINTSTRUCT ps;
			HDC hdc = BeginPaint(hwnd, &ps);

			// All painting occurs here, between BeginPaint and EndPaint.

			FillRect(hdc, &ps.rcPaint, (HBRUSH)(COLOR_WINDOW + 1));

			EndPaint(hwnd, &ps);
		}


	}

	return DefWindowProcW(hwnd, msg, wp, lp);
}

void Window::OnSize(HWND hwnd, UINT flag, int width, int height)
{

}
