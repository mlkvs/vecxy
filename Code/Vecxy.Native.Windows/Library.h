#pragma once

#define EXPORT extern "C" __declspec(dllexport)

#include "Window.h"

extern HINSTANCE LIB_INSTANCE;

// Window
EXPORT void* Window_Create(const wchar_t* className, const wchar_t* title, const int width, const int height);
EXPORT void Window_Destroy(void* ptr);
EXPORT bool Window_ProcessEvents(void* ptr);