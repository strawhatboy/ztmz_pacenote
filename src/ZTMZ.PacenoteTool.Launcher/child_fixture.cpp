#include "startup_core.h"
#include <windows.h>
using namespace startup;
int wmain(int argc, wchar_t** argv) {
    wchar_t path[32768]{};
    GetEnvironmentVariableW(L"ZTMZ_STARTUP_STATUS", path, 32768);
    const std::wstring mode = argc > 1 ? argv[1] : L"ready";
    if (mode == L"exit") return 73;
    Json report{{"pid", GetCurrentProcessId()}, {"stage", "configuration"}, {"state", "starting"}};
    report["arguments"] = Json::array();
    for (int i = 2; i < argc; ++i) report["arguments"].push_back(utf8(argv[i]));
    if (mode == L"failed") { report["state"] = "failed"; report["error"] = "fixture initialization failure"; }
    if (mode == L"ready") report["state"] = "ready";
    if (mode == L"stale") { report["state"] = "ready"; report["pid"] = 0; }
    writeJson(path, report);
    if (mode == L"failed") return 17;
    Sleep(60000); // the integration test owns and cleans up this fixture only
    return 0;
}
