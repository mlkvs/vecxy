#pragma once

#define EXPORT extern "C" __declspec(dllexport)

// Window
EXPORT void* Window_Create(const wchar_t* className);
EXPORT void Window_Destroy(void* handle);