#pragma once

#include <windows.h>

class Window
{
public:
	Window(const wchar_t* CLASS_NAME);
	~Window();

	HWND GetHandle() const;

	static LRESULT OnWindowProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam);

private:
	HINSTANCE _instance = nullptr;
	HWND _hwnd;
};
