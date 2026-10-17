// emulatorLauncher falso para probar Monedero sin RetroBat: se llama emulatorLauncher.exe, recibe
// -system <sis> -rom <ruta> (Monedero los lee de la línea de comandos), abre juego.exe (de su misma carpeta)
// como hijo y termina cuando el juego termina, como el de verdad con el sistema windows.
//   gcc -O2 -mwindows -o emulatorLauncher.exe launcher_falso.c
#include <windows.h>

int WINAPI WinMain(HINSTANCE i, HINSTANCE p, LPSTR c, int s) {
    wchar_t ruta[MAX_PATH];
    GetModuleFileNameW(NULL, ruta, MAX_PATH);
    wchar_t* barra = wcsrchr(ruta, L'\\');
    if (barra) wcscpy(barra + 1, L"juego.exe");
    STARTUPINFOW si = {sizeof si};
    PROCESS_INFORMATION pi;
    if (!CreateProcessW(ruta, NULL, NULL, NULL, FALSE, 0, NULL, NULL, &si, &pi)) return 1;
    WaitForSingleObject(pi.hProcess, INFINITE);
    return 0;
}
