#pragma once

#include <windows.h>
#include <string>

class Window
{
public:
	Window(const wchar_t* className, const wchar_t* title, int width, int height);
	~Window();

	HWND GetHandle() const;
	bool ProcessEvents();

	static LRESULT OnStaticWndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp);
	LRESULT CALLBACK OnWndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp);

private:
	const std::wstring _className;
	HWND _hwnd;

	static void OnSize(HWND hwnd, UINT flag, int width, int height);
};
