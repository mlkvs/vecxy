#include <stdexcept>

#include "Library.h"
#include "Window.h"

#define WGL_CONTEXT_MAJOR_VERSION_ARB 0x2091
#define WGL_CONTEXT_MINOR_VERSION_ARB 0x2092
#define WGL_CONTEXT_PROFILE_MASK_ARB  0x9126
#define WGL_CONTEXT_CORE_PROFILE_BIT_ARB 0x00000001

typedef HGLRC(WINAPI* PFNWGLCREATECONTEXTATTRIBSARBPROC)(HDC hDC, HGLRC hShareContext, const int* attribList);

Window::Window(const wchar_t* className, const wchar_t* title, const int width, const int height) : _className(className)
{
	WNDCLASSEXW ws = { sizeof(WNDCLASSEXW) };

	if(GetClassInfoExW(LIB_INSTANCE, _className.c_str(), &ws) == TRUE)
	{
		throw std::runtime_error("Failed to create window. Already created.");
	}

	ws.style = CS_OWNDC;
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

	_hdc = GetDC(_hwnd);

	CreateOpenGLContext();

	ShowWindow(_hwnd, SW_SHOW);
}

Window::~Window()
{
	if (_hglrc != nullptr)
	{
		wglMakeCurrent(nullptr, nullptr);
		wglDeleteContext(_hglrc);
	}

	if (_hwnd != nullptr)
	{
		DestroyWindow(_hwnd);
	}

	UnregisterClassW(_className.c_str(), LIB_INSTANCE);
}

void Window::Initialize()
{
	if(wglMakeCurrent(_hdc, _hglrc) == FALSE) {
		throw std::runtime_error("Failed to make OpenGL context current.");
	}
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

void Window::CreateOpenGLContext()
{
	PIXELFORMATDESCRIPTOR pfd = { sizeof(PIXELFORMATDESCRIPTOR), 1 };

	pfd.dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER;
	pfd.iPixelType = PFD_TYPE_RGBA;
	pfd.cColorBits = 32;
	pfd.cDepthBits = 24;
	pfd.cStencilBits = 8;
	pfd.iLayerType = PFD_MAIN_PLANE;

	const int pixelFormat = ChoosePixelFormat(_hdc, &pfd);
	SetPixelFormat(_hdc, pixelFormat, &pfd);

	const HGLRC tempContext = wglCreateContext(_hdc);
	wglMakeCurrent(_hdc, tempContext);

	const auto wglCreateContextAttribsARB = reinterpret_cast<PFNWGLCREATECONTEXTATTRIBSARBPROC>(wglGetProcAddress("wglCreateContextAttribsARB"));

	if (wglCreateContextAttribsARB != nullptr)
	{
		const int attribs[] = {
			WGL_CONTEXT_MAJOR_VERSION_ARB, 3,
			WGL_CONTEXT_MINOR_VERSION_ARB, 3,
			WGL_CONTEXT_PROFILE_MASK_ARB, WGL_CONTEXT_CORE_PROFILE_BIT_ARB,
			0
		};

		_hglrc = wglCreateContextAttribsARB(_hdc, nullptr, attribs);

		wglMakeCurrent(nullptr, nullptr);
		wglDeleteContext(tempContext);
	}
	else
	{
		wglMakeCurrent(nullptr, nullptr);
		_hglrc = tempContext;
	}
}

void* Window::GetProcAddress(const char* procName)
{
	if (procName == nullptr)
	{
		return nullptr;
	}

	auto proc = reinterpret_cast<void*>(::wglGetProcAddress(procName));

	const auto address = reinterpret_cast<uintptr_t>(proc);

	if
	(
		address == 0 || 
		address == 1 || 
		address == 2 || 
		address == 3 || 
		address == static_cast<uintptr_t>(-1)
	)
	{
		static HMODULE module = ::LoadLibraryA("opengl32.dll");
		proc = reinterpret_cast<void*>(::GetProcAddress(module, procName));
	}

	return proc;
}

void Window::SwapBuffers()
{
	::SwapBuffers(_hdc);
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

		case WM_ERASEBKGND:
		{
			return 1;
		}
		

		case WM_PAINT:
		{
			ValidateRect(hwnd, nullptr);

			return 0;
		}
	}

	return DefWindowProcW(hwnd, msg, wp, lp);
}

void Window::OnSize(HWND hwnd, UINT flag, int width, int height)
{

}
