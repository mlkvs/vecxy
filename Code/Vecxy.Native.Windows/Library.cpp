#include "Library.h"

HINSTANCE LIB_INSTANCE = nullptr;

BOOL APIENTRY DllMain(const HMODULE hModule, const DWORD ulReasonForCall, LPVOID lpReserved) {
    if(ulReasonForCall == DLL_PROCESS_ATTACH) {
        LIB_INSTANCE = hModule;
    }

    return TRUE;
}