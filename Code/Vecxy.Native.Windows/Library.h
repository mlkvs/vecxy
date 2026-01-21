#pragma once

#define EXPORT extern "C" __declspec(dllexport)

// Window
EXPORT void* Window_Create(const wchar_t* CLASS_NAME, const wchar_t* TITLE);
EXPORT void Window_Destroy(void* handle);