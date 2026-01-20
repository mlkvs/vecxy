#include <windows.h>

#define EXPORT extern "C" __declspec(dllexport)

// Глобальная переменная для хранения дескриптора именно этой DLL
HINSTANCE g_hInst = NULL;

// Точка входа в DLL (вызывается системой при загрузке библиотеки)
BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved) {
    if (ul_reason_for_call == DLL_PROCESS_ATTACH) {
        g_hInst = hModule;
    }
    return TRUE;
}

EXPORT void OpenNativeWindow(const wchar_t* id, const wchar_t* title) {
    
    const wchar_t* CLASS_NAME = id;

    WNDCLASSW wc = {};
    // Проверяем, не зарегистрирован ли класс уже (чтобы избежать ошибок при повторном вызове)
    if (!GetClassInfoW(g_hInst, CLASS_NAME, &wc)) {
        wc.lpfnWndProc = [](HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam) -> LRESULT {
            switch (uMsg) {
            case WM_DESTROY:
                PostQuitMessage(0);
                return 0;
            }
            return DefWindowProcW(hwnd, uMsg, wParam, lParam);
        };
        wc.hInstance = g_hInst;
        wc.lpszClassName = CLASS_NAME;
        wc.hCursor = LoadCursor(NULL, IDC_ARROW);
        //wc.hbrBackground = (HBRUSH)(COLOR_WINDOW + 1); // Добавим фон, иначе будет "прозрачно"

        if (!RegisterClassW(&wc)) return;
    }

    HWND hwnd = CreateWindowExW(
        0, CLASS_NAME, title,
        WS_OVERLAPPEDWINDOW | WS_VISIBLE,
        CW_USEDEFAULT, CW_USEDEFAULT, 1280, 720,
        NULL, NULL, g_hInst, NULL
    );

    if (hwnd == NULL) return;

    MSG msg = {};
    while (GetMessageW(&msg, NULL, 0, 0)) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
    
    // По хорошему, при закрытии одного окна стоит разрегистрировать класс, 
    // если вы планируете менять код DLL "на лету"
    UnregisterClassW(CLASS_NAME, g_hInst);
}