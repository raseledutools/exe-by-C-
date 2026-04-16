#include <windows.h>
#include <string>
#include <vector>
#include <sstream>
#include <TlHelp32.h>
#include <algorithm>

// --- C# থেকে কল করার জন্য Callback Type ---
typedef void(__stdcall* ViolationCallback)();

// --- গ্লোবাল ভেরিয়েবল ---
HHOOK hKeyboardHook = NULL;
ViolationCallback onViolationDetected = nullptr;
std::string keyBuffer = "";
std::vector<std::string> explicitWords;

// --- Helper Function: String Split ---
std::vector<std::string> SplitString(const std::string& s, char delimiter) {
    std::vector<std::string> tokens;
    std::string token;
    std::istringstream tokenStream(s);
    while (std::getline(tokenStream, token, delimiter)) {
        tokens.push_back(token);
    }
    return tokens;
}

// --- Keyboard Hook Logic ---
LRESULT CALLBACK LowLevelKeyboardProc(int nCode, WPARAM wParam, LPARAM lParam) {
    if (nCode >= 0 && wParam == WM_KEYDOWN) {
        KBDLLHOOKSTRUCT* pKeyBoard = (KBDLLHOOKSTRUCT*)lParam;
        char c = (char)pKeyBoard->vkCode;

        if (isalnum(c) || ispunct(c)) {
            keyBuffer += tolower(c);
            if (keyBuffer.length() > 50) keyBuffer.erase(0, 1);

            for (const auto& word : explicitWords) {
                if (keyBuffer.find(word) != std::string::npos) {
                    keyBuffer = ""; // Reset Buffer
                    
                    // C# এর Callback কল করে Overlay দেখানোর নির্দেশ দেওয়া
                    if (onViolationDetected != nullptr) {
                        onViolationDetected();
                    }

                    // Active Window Block/Close Tab (Ctrl+W)
                    HWND hActive = GetForegroundWindow();
                    if (hActive) {
                        SetForegroundWindow(hActive);
                        keybd_event(VK_CONTROL, 0, 0, 0);
                        keybd_event('W', 0, 0, 0);
                        keybd_event('W', 0, KEYEVENTF_KEYUP, 0);
                        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
                        ShowWindow(hActive, SW_MINIMIZE);
                    }
                    break;
                }
            }
        }
    }
    return CallNextHookEx(hKeyboardHook, nCode, wParam, lParam);
}

// =========================================================================
// C# (WPF) এর সাথে কানেক্ট করার জন্য Exported Functions (P/Invoke)
// =========================================================================
extern "C" {

    // ১. কীবোর্ড ট্র্যাকিং শুরু করা
    __declspec(dllexport) void __stdcall StartKeyboardFilter(ViolationCallback callback, const char* badWordsDelimited) {
        onViolationDetected = callback;
        explicitWords = SplitString(std::string(badWordsDelimited), '|');
        if (hKeyboardHook == NULL) {
            hKeyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, LowLevelKeyboardProc, GetModuleHandle(NULL), 0);
        }
    }

    // ২. কীবোর্ড ট্র্যাকিং বন্ধ করা
    __declspec(dllexport) void __stdcall StopKeyboardFilter() {
        if (hKeyboardHook != NULL) {
            UnhookWindowsHookEx(hKeyboardHook);
            hKeyboardHook = NULL;
        }
    }

    // ৩. নির্দিষ্ট ওয়েবসাইট ব্লক করা (Ctrl+W)
    __declspec(dllexport) void __stdcall ForceBlockTab(HWND hwnd) {
        SetForegroundWindow(hwnd);
        keybd_event(VK_CONTROL, 0, 0, 0);
        keybd_event('W', 0, 0, 0);
        keybd_event('W', 0, KEYEVENTF_KEYUP, 0);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
        ShowWindow(hwnd, SW_MINIMIZE);
    }

    // ৪. Active Window Handle নেওয়া
    __declspec(dllexport) HWND __stdcall GetActiveWindowHandle() {
        return GetForegroundWindow();
    }

    // ৫. Active Window এর টাইটেল নেওয়া
    __declspec(dllexport) void __stdcall GetActiveTitle(HWND hwnd, char* buffer, int maxCount) {
        GetWindowTextA(hwnd, buffer, maxCount);
    }

    // ৬. ব্যাকগ্রাউন্ড প্রসেস কিলিং (পাওয়ারফুল লজিক)
    __declspec(dllexport) void __stdcall KillTargetProcesses(const char* targetsDelimited, bool isAllowMode, const char* systemAppsDelimited) {
        std::vector<std::string> targets = SplitString(std::string(targetsDelimited), '|');
        std::vector<std::string> sysApps = SplitString(std::string(systemAppsDelimited), '|');

        HANDLE hSnap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (hSnap == INVALID_HANDLE_VALUE) return;

        PROCESSENTRY32 pe32;
        pe32.dwSize = sizeof(PROCESSENTRY32);

        if (Process32First(hSnap, &pe32)) {
            do {
                std::string pName = pe32.szExeFile;
                std::transform(pName.begin(), pName.end(), pName.begin(), ::tolower);

                if (pName == "taskmgr.exe" || pName == "msiexec.exe") {
                    HANDLE hProcess = OpenProcess(PROCESS_TERMINATE, FALSE, pe32.th32ProcessID);
                    if (hProcess) { TerminateProcess(hProcess, 0); CloseHandle(hProcess); }
                    continue;
                }

                if (isAllowMode) {
                    // Allow mode: যদি সিস্টেম অ্যাপ না হয় এবং অ্যালাউ লিস্টেও না থাকে, তবে কিল করো!
                    bool isSys = std::find(sysApps.begin(), sysApps.end(), pName) != sysApps.end();
                    bool isAllowed = std::find(targets.begin(), targets.end(), pName) != targets.end();
                    if (!isSys && !isAllowed) {
                        HANDLE hProcess = OpenProcess(PROCESS_TERMINATE, FALSE, pe32.th32ProcessID);
                        if (hProcess) { TerminateProcess(hProcess, 0); CloseHandle(hProcess); }
                    }
                } else {
                    // Block mode: যদি ব্লক লিস্টে থাকে, তবে কিল করো!
                    if (std::find(targets.begin(), targets.end(), pName) != targets.end()) {
                        HANDLE hProcess = OpenProcess(PROCESS_TERMINATE, FALSE, pe32.th32ProcessID);
                        if (hProcess) { TerminateProcess(hProcess, 0); CloseHandle(hProcess); }
                    }
                }
            } while (Process32Next(hSnap, &pe32));
        }
        CloseHandle(hSnap);
    }
}
