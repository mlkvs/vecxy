#pragma once

#include <windows.h>

class Window
{
public:
	Window(const wchar_t* CLASS_NAME, const wchar_t* TITLE);
	~Window();

	HWND GetHandle() const;

	static LRESULT OnWindowProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam);

private:
	HINSTANCE _instance = nullptr;
	HWND _hwnd;

	static void OnSize(HWND hwnd, UINT flag, int width, int height);
};
