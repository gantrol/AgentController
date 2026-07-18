#include <windows.h>
#include <newdev.h>
#include <setupapi.h>
#include <strsafe.h>

#include <cwchar>
#include <memory>
#include <string>
#include <vector>

namespace {

constexpr wchar_t DefaultHardwareId[] = L"Root\\CodexMicroHidUm";

void PrintError(const wchar_t* operation, DWORD error)
{
    wchar_t* message = nullptr;
    const DWORD flags = FORMAT_MESSAGE_ALLOCATE_BUFFER |
        FORMAT_MESSAGE_FROM_SYSTEM |
        FORMAT_MESSAGE_IGNORE_INSERTS;
    FormatMessageW(
        flags,
        nullptr,
        error,
        0,
        reinterpret_cast<wchar_t*>(&message),
        0,
        nullptr);
    fwprintf(
        stderr,
        L"%s failed (0x%08lX): %s\n",
        operation,
        error,
        message != nullptr ? message : L"Unknown error");
    if (message != nullptr) {
        LocalFree(message);
    }
}

bool UpdateExisting(
    const wchar_t* infPath,
    const wchar_t* hardwareId,
    BOOL* rebootRequired)
{
    if (UpdateDriverForPlugAndPlayDevicesW(
        nullptr,
        hardwareId,
        infPath,
        INSTALLFLAG_FORCE,
        rebootRequired)) {
        return true;
    }

    const DWORD error = GetLastError();
    if (error == ERROR_NO_SUCH_DEVINST) {
        return false;
    }

    PrintError(L"UpdateDriverForPlugAndPlayDevices", error);
    ExitProcess(error);
}

bool CreateRootDevice(
    const wchar_t* infPath,
    const wchar_t* hardwareId,
    BOOL* rebootRequired)
{
    GUID classGuid{};
    wchar_t className[256]{};
    if (!SetupDiGetINFClassW(
        infPath,
        &classGuid,
        className,
        ARRAYSIZE(className),
        nullptr)) {
        PrintError(L"SetupDiGetINFClass", GetLastError());
        return false;
    }

    HDEVINFO deviceInfoSet = SetupDiCreateDeviceInfoList(
        &classGuid,
        nullptr);
    if (deviceInfoSet == INVALID_HANDLE_VALUE) {
        PrintError(L"SetupDiCreateDeviceInfoList", GetLastError());
        return false;
    }

    SP_DEVINFO_DATA deviceInfo{};
    deviceInfo.cbSize = sizeof(deviceInfo);
    bool registered = false;
    bool success = false;
    do {
        if (!SetupDiCreateDeviceInfoW(
            deviceInfoSet,
            className,
            &classGuid,
            nullptr,
            nullptr,
            DICD_GENERATE_ID,
            &deviceInfo)) {
            PrintError(L"SetupDiCreateDeviceInfo", GetLastError());
            break;
        }

        const size_t hardwareIdCharacters = wcslen(hardwareId);
        if (hardwareIdCharacters == 0 ||
            hardwareIdCharacters > (MAXDWORD / sizeof(wchar_t)) - 2) {
            fwprintf(stderr, L"The hardware ID is invalid.\n");
            break;
        }

        // SPDRP_HARDWAREID is REG_MULTI_SZ, so retain two trailing NULs.
        std::vector<wchar_t> hardwareIds(hardwareIdCharacters + 2, L'\0');
        if (FAILED(StringCchCopyW(
            hardwareIds.data(),
            hardwareIds.size(),
            hardwareId))) {
            fwprintf(stderr, L"Unable to construct the hardware ID.\n");
            break;
        }
        const DWORD hardwareIdBytes = static_cast<DWORD>(
            hardwareIds.size() * sizeof(wchar_t));

        if (!SetupDiSetDeviceRegistryPropertyW(
            deviceInfoSet,
            &deviceInfo,
            SPDRP_HARDWAREID,
            reinterpret_cast<const BYTE*>(hardwareIds.data()),
            hardwareIdBytes)) {
            PrintError(
                L"SetupDiSetDeviceRegistryProperty",
                GetLastError());
            break;
        }

        if (!SetupDiCallClassInstaller(
            DIF_REGISTERDEVICE,
            deviceInfoSet,
            &deviceInfo)) {
            PrintError(L"DIF_REGISTERDEVICE", GetLastError());
            break;
        }
        registered = true;

        if (!UpdateDriverForPlugAndPlayDevicesW(
            nullptr,
            hardwareId,
            infPath,
            INSTALLFLAG_FORCE,
            rebootRequired)) {
            PrintError(
                L"UpdateDriverForPlugAndPlayDevices",
                GetLastError());
            break;
        }

        success = true;
    } while (false);

    if (!success && registered) {
        SetupDiCallClassInstaller(
            DIF_REMOVE,
            deviceInfoSet,
            &deviceInfo);
    }
    SetupDiDestroyDeviceInfoList(deviceInfoSet);
    return success;
}

} // namespace

int wmain(int argc, wchar_t** argv)
{
    if ((argc != 3 && argc != 4) ||
        _wcsicmp(argv[1], L"install") != 0) {
        fwprintf(
            stderr,
            L"Usage: CodexMicro.DriverInstaller.Native install "
            L"<absolute-inf-path> [hardware-id]\n");
        return 2;
    }

    const wchar_t* hardwareId = argc == 4
        ? argv[3]
        : DefaultHardwareId;
    if (_wcsnicmp(hardwareId, L"Root\\", 5) != 0 ||
        wcslen(hardwareId) <= 5) {
        fwprintf(stderr, L"Only a concrete Root\\ hardware ID is accepted.\n");
        return 2;
    }

    wchar_t infPath[MAX_PATH]{};
    if (GetFullPathNameW(
        argv[2],
        ARRAYSIZE(infPath),
        infPath,
        nullptr) == 0 ||
        GetFileAttributesW(infPath) == INVALID_FILE_ATTRIBUTES) {
        PrintError(L"Resolve INF", GetLastError());
        return 1;
    }

    BOOL rebootRequired = FALSE;
    if (!UpdateExisting(infPath, hardwareId, &rebootRequired) &&
        !CreateRootDevice(infPath, hardwareId, &rebootRequired)) {
        return 1;
    }

    wprintf(
        rebootRequired
            ? L"Codex Micro HID %s installed; Windows requested a restart.\n"
            : L"Codex Micro HID %s installed and started.\n",
        hardwareId);
    return 0;
}
