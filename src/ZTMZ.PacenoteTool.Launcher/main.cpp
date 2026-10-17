#include "startup_core.h"
#include <windows.h>
#include <commctrl.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <shellapi.h>
#include <algorithm>
#include <array>
#include <fstream>
#include <memory>
#include <sstream>

using namespace startup;
namespace {
constexpr auto registryKey = L"Software\\ZTMZ Next Generation Pacenote Tool";
constexpr auto hostName = L"ZTMZ.PacenoteTool.WpfGUI.exe";
#ifdef ZTMZ_STARTUP_TEST_MUTEX
constexpr auto mutexName = L"ZTMZ-startup-integration-fixture";
#else
constexpr auto mutexName = L"{F1D4A3D4-6D1A-4D3A-8D1A-4D1A6D1A4D3A}";
#endif
constexpr int retryButton = 100, reportButton = 101, folderButton = 102, runtimeButton = 103,
    resetButton = 104, continueButton = 105, waitButton = 106;
struct Handle {
    HANDLE value = nullptr;
    explicit Handle(HANDLE v = nullptr) : value(v) {}
    ~Handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
};
struct Issue {
    std::string kind;
    std::wstring summary, details;
    std::vector<fs::path> resetFiles;
};
struct Run {
    fs::path app, session, statusPath, reportPath;
    DataLocation data;
    Json report = Json::object();
    bool chinese = PRIMARYLANGID(GetUserDefaultUILanguage()) == LANG_CHINESE;
    bool headless = false;
    unsigned long long timeoutMs = 60000;
    std::vector<std::wstring> arguments;
    std::wstring t(const wchar_t* en, const wchar_t* zh) const { return chinese ? zh : en; }
    void save() const { writeJson(reportPath, report); }
};
std::wstring env(const wchar_t* name) {
    DWORD size = GetEnvironmentVariableW(name, nullptr, 0);
    if (!size) return {};
    std::wstring result(size, L'\0');
    DWORD read = GetEnvironmentVariableW(name, result.data(), size);
    result.resize(read < size ? read : 0);
    return result;
}
std::wstring registry(HKEY hive, const wchar_t* key, const wchar_t* name, REGSAM view = 0, bool strict = false) {
    HKEY handle{};
    auto opened = RegOpenKeyExW(hive, key, 0, KEY_READ | view, &handle);
    if (opened != ERROR_SUCCESS) {
        if (strict && opened != ERROR_FILE_NOT_FOUND && opened != ERROR_PATH_NOT_FOUND)
            throw std::runtime_error("Cannot read ZTMZHome registry key (Windows error " + std::to_string(opened) + ")");
        return {};
    }
    DWORD size = 0, type = 0;
    auto result = RegQueryValueExW(handle, name, nullptr, &type, nullptr, &size);
    if (result != ERROR_SUCCESS || (type != REG_SZ && type != REG_EXPAND_SZ) || size > 32768) {
        RegCloseKey(handle);
        if (strict && result != ERROR_FILE_NOT_FOUND) throw std::runtime_error("Cannot read ZTMZHome: inaccessible value or invalid registry type/size");
        return {};
    }
    std::wstring value(size / sizeof(wchar_t) + 1, L'\0');
    result = RegQueryValueExW(handle, name, nullptr, &type, reinterpret_cast<BYTE*>(value.data()), &size);
    RegCloseKey(handle);
    if (result != ERROR_SUCCESS) { if (strict) throw std::runtime_error("Cannot read ZTMZHome registry value"); return {}; }
    value.resize(wcslen(value.c_str()));
    if (type == REG_EXPAND_SZ) {
        auto count = ExpandEnvironmentStringsW(value.c_str(), nullptr, 0);
        if (!count || count > 32768) throw std::runtime_error("Cannot expand registry path");
        std::wstring expanded(count, L'\0');
        auto written = ExpandEnvironmentStringsW(value.c_str(), expanded.data(), count);
        if (!written || written > count) throw std::runtime_error("Cannot expand registry path");
        expanded.resize(written - 1);
        return expanded;
    }
    return value;
}
fs::path knownFolder(REFKNOWNFOLDERID id) {
    PWSTR value = nullptr;
    if (FAILED(SHGetKnownFolderPath(id, 0, nullptr, &value))) throw std::runtime_error("Cannot resolve Windows user folder");
    fs::path result(value); CoTaskMemFree(value); return result;
}
std::wstring uniqueId() {
    GUID id{}; if (FAILED(CoCreateGuid(&id))) throw std::runtime_error("Cannot create startup session ID");
    wchar_t buffer[40]{}; StringFromGUID2(id, buffer, 40); return buffer;
}
void openPath(const fs::path& path) { ShellExecuteW(nullptr, L"open", path.c_str(), nullptr, nullptr, SW_SHOWNORMAL); }
void recordIssue(Run& run, const Issue& issue) {
    run.report["state"] = issue.kind;
    run.report["summary"] = utf8(issue.summary);
    run.report["details"] = utf8(issue.details);
    run.save();
}
std::wstring pathList(const std::vector<fs::path>& files) {
    std::wstring result;
    for (size_t i = 0; i < files.size() && i < 12; ++i) result += files[i].wstring() + L"\n";
    if (files.size() > 12) result += L"...\n";
    return result;
}
std::vector<fs::path> configFiles(const fs::path& root) {
    std::vector<fs::path> result{root / L"config.json", root / L"userconfig.json"};
    if (fs::is_directory(root / L"games")) {
        for (const auto& entry : fs::directory_iterator(root / L"games"))
            if (entry.is_regular_file() && entry.path().extension() == L".json") result.push_back(entry.path());
    }
    return result;
}
std::wstring hostError;
void __cdecl hostErrorWriter(const wchar_t* text) {
    if (hostError.size() < 32768 && text) hostError += std::wstring(text) + L"\n";
}
std::array<int, 3> version(const fs::path& path) {
    std::array<int, 3> values{};
    swscanf_s(path.filename().c_str(), L"%d.%d.%d", &values[0], &values[1], &values[2]);
    return values;
}
bool runtimeAvailable(Run& run) {
    // Use the actual hostfxr resolver and this application's runtimeconfig, so
    // framework versions, architecture and roll-forward rules match .NET itself.
    std::vector<fs::path> roots;
    for (auto name : {L"DOTNET_ROOT_X64", L"DOTNET_ROOT"}) if (!env(name).empty()) { roots.emplace_back(env(name)); break; }
    if (roots.empty()) {
        auto installed = registry(HKEY_LOCAL_MACHINE, L"SOFTWARE\\dotnet\\Setup\\InstalledVersions\\x64", L"InstallLocation", KEY_WOW64_64KEY);
        roots.emplace_back(installed.empty() ? knownFolder(FOLDERID_ProgramFilesX64) / L"dotnet" : fs::path(installed));
    }
    hostError.clear();
    for (const auto& root : roots) {
        std::vector<fs::path> versions;
        std::error_code error;
        auto fxr = root / L"host" / L"fxr";
        if (!fs::is_directory(fxr, error)) continue;
        for (const auto& entry : fs::directory_iterator(fxr)) if (entry.is_directory()) versions.push_back(entry.path());
        std::sort(versions.begin(), versions.end(), [](const auto& a, const auto& b) { return version(a) > version(b); });
        if (versions.empty()) continue;
        auto library = LoadLibraryExW((versions.front() / L"hostfxr.dll").c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        if (!library) { hostError = L"Cannot load the x64 hostfxr.dll. Windows error " + std::to_wstring(GetLastError()); continue; }
        using Init = int(__cdecl*)(const wchar_t*, const void*, void**);
        using Close = int(__cdecl*)(void*);
        using Writer = void(__cdecl*)(const wchar_t*);
        using SetWriter = Writer(__cdecl*)(Writer);
        auto initialize = reinterpret_cast<Init>(GetProcAddress(library, "hostfxr_initialize_for_runtime_config"));
        auto close = reinterpret_cast<Close>(GetProcAddress(library, "hostfxr_close"));
        auto setWriter = reinterpret_cast<SetWriter>(GetProcAddress(library, "hostfxr_set_error_writer"));
        bool okay = false;
        if (initialize && close && setWriter) {
            const auto previous = setWriter(hostErrorWriter);
            void* context = nullptr;
            int code = initialize((run.app / L"ZTMZ.PacenoteTool.WpfGUI.runtimeconfig.json").c_str(), nullptr, &context);
            if (context) close(context);
            setWriter(previous);
            run.report["runtimeResolverCode"] = code;
            run.report["dotnetRoot"] = utf8(root.wstring());
            okay = code >= 0;
        }
        FreeLibrary(library);
        if (okay) return true;
    }
    return false;
}
bool webViewAvailable(const fs::path& root) {
    auto loader = root / L"WebView2Loader.dll";
    if (!fs::exists(loader)) loader = root / L"runtimes" / L"win-x64" / L"native" / L"WebView2Loader.dll";
    auto module = LoadLibraryExW(loader.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    if (!module) return false;
    using GetVersion = HRESULT(STDAPICALLTYPE*)(PCWSTR, PWSTR*);
    auto getVersion = reinterpret_cast<GetVersion>(GetProcAddress(module, "GetAvailableCoreWebView2BrowserVersionString"));
    PWSTR text = nullptr;
    bool available = getVersion && SUCCEEDED(getVersion(nullptr, &text)) && text && *text;
    CoTaskMemFree(text); FreeLibrary(module); return available;
}
std::optional<Issue> preflight(Run& run) {
    // Registry/data-folder resolution is the first application configuration check.
    try {
        const auto configured = env(L"ZTMZ_DATA_HOME");
        const auto saved = configured.find_first_not_of(L" \t\r\n") == std::wstring::npos
            ? registry(HKEY_CURRENT_USER, registryKey, L"ZTMZHome", 0, true) : std::wstring{};
        const bool hasConfiguredPath = configured.find_first_not_of(L" \t\r\n") != std::wstring::npos || saved.find_first_not_of(L" \t\r\n") != std::wstring::npos;
        run.data = resolveData(configured, saved, hasConfiguredPath ? fs::path{} : knownFolder(FOLDERID_Documents));
    } catch (const std::exception& ex) {
        return Issue{"registry", run.t(L"Cannot resolve ZTMZ's configured data folder", L"无法读取 ZTMZ 数据文件夹配置"), wide(ex.what())};
    }
    run.report["dataFolder"] = utf8(run.data.path.wstring());
    run.report["dataFolderSource"] = run.data.source;
    run.report["registeredInstallDir"] = utf8(registry(HKEY_CURRENT_USER, registryKey, L"InstallDir"));
    run.report["applicationFolder"] = utf8(run.app.wstring());
    run.report["stage"] = "data_folder"; run.save();
    try { checkWritable(run.data.path); }
    catch (const std::exception& ex) {
        return Issue{"data_folder", run.t(L"ZTMZ's data folder is unavailable", L"ZTMZ 数据文件夹不可用"),
            run.data.path.wstring() + L"\n\n" + run.t(L"Check the configured location and permissions, or choose your existing ZTMZ data folder. No settings have been moved or reset.", L"请检查配置的路径及权限，或选择已有的 ZTMZ 数据文件夹。未移动或重置任何设置。") + L"\n" + wide(ex.what())};
    }
    run.report["stage"] = "configuration"; run.save();
    for (const auto& path : configFiles(run.data.path)) {
        bool valid = false;
        try { valid = validConfiguration(path, path.filename() != L"userconfig.json"); }
        catch (const Json::exception&) {
            // Legacy/base and game settings use Json.NET, which accepts syntax
            // such as trailing commas and single quotes. Let that parser decide;
            // only userconfig.json uses the strict System.Text.Json contract.
            if (path.filename() != L"userconfig.json") continue;
        }
        catch (const std::exception&) {
            return Issue{"configuration", run.t(L"Cannot read a settings file", L"无法读取设置文件"), path.wstring() + L"\n" + run.t(L"Check file permissions and whether another program has locked it.", L"请检查文件权限，以及是否被其他程序锁定。")};
        }
        if (!valid) return Issue{"configuration", run.t(L"A settings file is damaged", L"设置文件已损坏"),
            path.wstring() + L"\n\n" + run.t(L"The file is not a JSON object. You can back up only this file and let ZTMZ recreate its defaults. Voice packages, scripts and replays are kept.", L"该文件不是有效的 JSON 对象。可备份此文件后让 ZTMZ 重新生成默认设置。语音包、路书和回放会保留。"), {path}};
    }
    // Follow the selected app language once configuration can be read safely.
    for (const auto* name : {L"config.json", L"userconfig.json"}) {
        auto path = run.data.path / name;
        if (fs::exists(path)) {
            try {
                auto config = readJson(path, path.filename() != L"userconfig.json");
                if (config.contains("Language") && config["Language"].is_string()) run.chinese = config["Language"].get<std::string>().starts_with("zh");
            } catch (const Json::exception&) { /* Json.NET will handle legacy syntax. */ }
        }
    }
    run.report["stage"] = "files"; run.save();
    Json manifest;
    try {
        manifest = readJson(run.app / L"ZTMZ.startup.json");
        if (manifest.at("schemaVersion") != 1) throw std::runtime_error("Unknown startup manifest version");
        auto missing = missingFiles(run.app, manifest.at("applicationFiles"));
        if (!missing.empty()) return Issue{"files", run.t(L"ZTMZ's installation is incomplete", L"ZTMZ 安装文件不完整"),
            run.t(L"Reinstall ZTMZ to restore these files. If security software quarantined them, review its report.\n\n", L"请重新安装 ZTMZ 以恢复这些文件。如果安全软件隔离了文件，请查看其报告。\n\n") + pathList(missing)};
        missing = missingFiles(run.data.path, manifest.at("dataFiles"));
        if (!missing.empty()) return Issue{"data_folder", run.t(L"Required data files were not found", L"未找到必需的数据文件"),
            run.t(L"Choose the existing ZTMZ data folder, or reinstall to restore the packaged data.\n\n", L"请选择已有的 ZTMZ 数据文件夹，或重新安装以恢复随程序提供的数据。\n\n") + pathList(missing)};
    } catch (const std::exception& ex) {
        return Issue{"files", run.t(L"The startup manifest is missing or damaged", L"启动检查清单丢失或损坏"),
            run.t(L"Reinstall ZTMZ.\n", L"请重新安装 ZTMZ。\n") + wide(ex.what())};
    }
    run.report["stage"] = "runtime"; run.save();
    if (!runtimeAvailable(run)) return Issue{"runtime", run.t(L"The required .NET runtime is unavailable", L"所需的 .NET 运行时不可用"),
        run.t(L"Install the x64 .NET 10 Desktop and ASP.NET Core runtimes, then retry. An x86 runtime is not sufficient. Re-running the ZTMZ installer can install both prerequisites.\n\n", L"请安装 x64 .NET 10 桌面运行时和 ASP.NET Core 运行时后重试。仅安装 x86 运行时不够。重新运行 ZTMZ 安装程序可安装这两个组件。\n\n") + hostError};
    if (!webViewAvailable(run.app)) return Issue{"webview", run.t(L"Microsoft Edge WebView2 is unavailable", L"Microsoft Edge WebView2 不可用"),
        run.t(L"The embedded editor and web pages need WebView2. Install it, or continue with those features unavailable.", L"内嵌编辑器和网页需要 WebView2。请安装它，或继续启动（相关功能将不可用）。")};
    return {};
}
bool selectDataFolder(Run& run) {
    IFileOpenDialog* dialog = nullptr;
    if (FAILED(CoCreateInstance(CLSID_FileOpenDialog, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&dialog)))) return false;
    dialog->SetOptions(FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST | FOS_NOCHANGEDIR);
    dialog->SetTitle(run.t(L"Select your existing ZTMZ data folder", L"选择已有的 ZTMZ 数据文件夹").c_str());
    IShellItem* item = nullptr;
    PWSTR path = nullptr;
    bool chosen = SUCCEEDED(dialog->Show(nullptr)) && SUCCEEDED(dialog->GetResult(&item)) && SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path));
    std::wstring selection = chosen ? path : L"";
    CoTaskMemFree(path); if (item) item->Release(); dialog->Release();
    if (!chosen) return false;
    checkWritable(selection);
    if (!fs::is_regular_file(fs::path(selection) / L"data.zdb")) throw std::runtime_error("Selected folder has no data.zdb; the configured folder was not changed");
    const bool temporary = run.data.source == "environment";
    auto prompt = run.t(L"Use this data folder?\n\n", L"使用此数据文件夹？\n\n") + selection + L"\n\n" +
        (temporary ? run.t(L"This overrides ZTMZ_DATA_HOME only for this launch.", L"仅为本次启动替换 ZTMZ_DATA_HOME。") : run.t(L"This updates ZTMZHome in your user registry. Existing files will not be moved.", L"将更新当前用户注册表中的 ZTMZHome，不会移动已有文件。"));
    if (MessageBoxW(nullptr, prompt.c_str(), L"ZTMZ", MB_OKCANCEL | MB_ICONQUESTION) != IDOK) return false;
    if (temporary) SetEnvironmentVariableW(L"ZTMZ_DATA_HOME", selection.c_str());
    else {
        HKEY key{};
        if (RegCreateKeyExW(HKEY_CURRENT_USER, registryKey, 0, nullptr, 0, KEY_SET_VALUE, nullptr, &key, nullptr) != ERROR_SUCCESS)
            throw std::runtime_error("Cannot update ZTMZHome in the user registry");
        auto result = RegSetValueExW(key, L"ZTMZHome", 0, REG_SZ, reinterpret_cast<const BYTE*>(selection.c_str()), static_cast<DWORD>((selection.size() + 1) * sizeof(wchar_t)));
        RegCloseKey(key);
        if (result != ERROR_SUCCESS) throw std::runtime_error("Could not save the selected data folder");
    }
    return true;
}
int showIssue(Run& run, const Issue& issue, bool childRunning = false) {
    recordIssue(run, issue);
    if (run.headless) return IDCANCEL;
    std::vector<std::wstring> labels;
    std::vector<int> ids;
    auto add = [&](int id, std::wstring text) { ids.push_back(id); labels.push_back(std::move(text)); };
    if (issue.kind == "slow") add(waitButton, run.t(L"Keep waiting", L"继续等待"));
    else if (!childRunning) add(retryButton, run.t(L"Retry", L"重试"));
    add(reportButton, run.t(L"Open startup report", L"打开启动报告"));
    if (!childRunning && issue.kind == "data_folder") add(folderButton, run.t(L"Choose existing data folder", L"选择已有的数据文件夹"));
    if (issue.kind == "runtime" || issue.kind == "webview") add(runtimeButton, run.t(L"Open runtime download page", L"打开运行时下载页面"));
    if (issue.kind == "webview") add(continueButton, run.t(L"Continue without WebView2", L"不使用 WebView2 继续启动"));
    if (!childRunning && !issue.resetFiles.empty()) add(resetButton, run.t(L"Back up and reset these settings", L"备份并重置这些设置"));
    std::vector<TASKDIALOG_BUTTON> buttons;
    for (size_t i = 0; i < ids.size(); ++i) buttons.push_back({ids[i], labels[i].c_str()});
    TASKDIALOGCONFIG config{sizeof(config)};
    config.dwFlags = TDF_SIZE_TO_CONTENT | TDF_ALLOW_DIALOG_CANCELLATION;
    config.pszWindowTitle = L"ZTMZ — Startup";
    config.pszMainIcon = TD_WARNING_ICON;
    config.pszMainInstruction = issue.summary.c_str();
    auto details = issue.details + L"\n\n" + run.t(L"Report: ", L"报告：") + run.reportPath.wstring();
    if (childRunning) details += L"\n" + run.t(L"ZTMZ is still running. Closing this dialog leaves it running; settings cannot be reset until it exits.", L"ZTMZ 仍在运行。关闭此对话框不会结束它；退出 ZTMZ 后才可重置设置。");
    config.pszContent = details.c_str();
    config.cButtons = static_cast<UINT>(buttons.size()); config.pButtons = buttons.data();
    config.dwCommonButtons = TDCBF_CLOSE_BUTTON;
    config.nDefaultButton = IDCLOSE;
    int result = IDCANCEL;
    if (FAILED(TaskDialogIndirect(&config, &result, nullptr, nullptr))) MessageBoxW(nullptr, details.c_str(), issue.summary.c_str(), MB_OK | MB_ICONERROR);
    if (result == reportButton) openPath(run.reportPath);
    if (result == runtimeButton) ShellExecuteW(nullptr, L"open", issue.kind == "webview" ? L"https://developer.microsoft.com/microsoft-edge/webview2/" : L"https://dotnet.microsoft.com/download/dotnet/10.0", nullptr, nullptr, SW_SHOWNORMAL);
    return result;
}
bool handleIssue(Run& run, const Issue& issue) {
    for (;;) {
        int action = showIssue(run, issue);
        if (action == retryButton) return true;
        if (action == folderButton) { if (selectDataFolder(run)) return true; }
        else if (action == resetButton) {
            auto text = pathList(issue.resetFiles) + L"\n" + run.t(L"Back up these files beside the originals and restore default settings on the next start? This cannot repair missing program files.", L"将这些文件备份到原位置旁，并在下次启动时恢复默认设置？此操作不能修复缺失的程序文件。");
            if (MessageBoxW(nullptr, text.c_str(), L"ZTMZ", MB_OKCANCEL | MB_ICONQUESTION) == IDOK) {
                for (const auto& file : issue.resetFiles) {
                    auto backup = backupConfiguration(file, run.data.path, std::to_wstring(GetCurrentProcessId()) + L"-" + std::to_wstring(GetTickCount64()));
                    run.report["configurationBackups"].push_back(utf8(backup.wstring())); run.save();
                }
                return true;
            }
        } else if (action != reportButton && action != runtimeButton) return false;
    }
}
int launchAndMonitor(Run& run) {
    SECURITY_ATTRIBUTES security{sizeof(security), nullptr, TRUE};
    Handle log(CreateFileW((run.session / L"host-output.log").c_str(), GENERIC_WRITE, FILE_SHARE_READ, &security, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr));
    Handle input(CreateFileW(L"NUL", GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, &security, OPEN_EXISTING, 0, nullptr));
    if (log.value == INVALID_HANDLE_VALUE || input.value == INVALID_HANDLE_VALUE) throw std::runtime_error("Cannot create startup output log");
    SetEnvironmentVariableW(L"ZTMZ_STARTUP_STATUS", run.statusPath.c_str());
    std::wstring command = quote((run.app / hostName).wstring());
    for (const auto& argument : run.arguments) command += L" " + quote(argument);
    STARTUPINFOW start{sizeof(start)}; start.dwFlags = STARTF_USESTDHANDLES;
    // NLog already writes normal output to the application's rotating logs.
    // Capture host errors only, avoiding a second unbounded runtime log.
    start.hStdOutput = input.value; start.hStdError = log.value; start.hStdInput = input.value;
    PROCESS_INFORMATION process{};
    if (!CreateProcessW((run.app / hostName).c_str(), command.data(), nullptr, nullptr, TRUE, 0, nullptr, run.app.c_str(), &start, &process)) {
        auto code = GetLastError();
        return handleIssue(run, {"launch_failed", run.t(L"Windows could not start ZTMZ", L"Windows 无法启动 ZTMZ"), L"Windows error " + std::to_wstring(code)}) ? 10 : 2;
    }
    Handle child(process.hProcess), thread(process.hThread);
    run.report["childProcessId"] = process.dwProcessId;
    run.report["state"] = "starting"; run.save();
    auto since = GetTickCount64();
    Json status = Json::object();
    for (;;) {
        bool exited = WaitForSingleObject(child.value, 100) == WAIT_OBJECT_0;
        try {
            if (fs::exists(run.statusPath)) {
                auto candidate = readJson(run.statusPath);
                if (candidate.is_object() && candidate.value("pid", 0UL) == process.dwProcessId &&
                    candidate.contains("state") && candidate["state"].is_string() &&
                    candidate.contains("stage") && candidate["stage"].is_string()) status = std::move(candidate);
            }
        } catch (...) { /* atomic replacement can briefly race a read; retry */ }
        auto decision = decide(status, exited, GetTickCount64() - since, run.timeoutMs);
        if (decision == Decision::Waiting) continue;
        run.report["managedStartup"] = status;
        if (decision == Decision::Ready) { run.report["state"] = "ready"; run.save(); return 0; }
        DWORD exitCode = STILL_ACTIVE; GetExitCodeProcess(child.value, &exitCode);
        run.report["exitCode"] = exitCode;
        auto details = run.t(L"Last startup stage: ", L"最后的启动阶段：") + wide(status.value("stage", std::string("before_managed_entry")));
        if (status.contains("error") && status["error"].is_string()) details += L"\n\n" + wide(status.value("error", std::string{})).substr(0, 6000);
        wchar_t code[32]{}; swprintf_s(code, L"0x%08lX", exitCode);
        if (exited) details += L"\nExit code: " + std::wstring(code);
        Issue issue{decision == Decision::Slow ? "slow" : "startup_failed",
            decision == Decision::Slow ? run.t(L"ZTMZ is taking longer to start", L"ZTMZ 启动时间较长") : run.t(L"ZTMZ could not finish starting", L"ZTMZ 未能完成启动"), details};
        if (decision == Decision::Failed && !exited) exited = WaitForSingleObject(child.value, 2000) == WAIT_OBJECT_0;
        if (exited) return handleIssue(run, issue) ? 10 : 2;
        for (;;) {
            int action = showIssue(run, issue, true);
            if (action == waitButton) { since = GetTickCount64(); break; }
            if (action != reportButton) return 3; // never terminate a slow application automatically
        }
    }
}
int execute(Run& run, bool diagnose) {
    for (;;) {
        if (fs::exists(run.statusPath)) fs::remove(run.statusPath);
        auto issue = preflight(run);
        if (issue) {
            if (issue->kind == "webview") {
                run.report["webView2Available"] = false;
                if (diagnose) { recordIssue(run, *issue); return 2; }
                int action;
                do { action = showIssue(run, *issue); } while (action == reportButton || action == runtimeButton);
                if (action == retryButton) continue;
                if (action != continueButton) return 2;
            } else {
                if (handleIssue(run, *issue)) continue;
                return 2;
            }
        } else run.report["webView2Available"] = true;
        if (diagnose) { run.report["state"] = "checks_passed"; run.save(); return 0; }
        int result = launchAndMonitor(run);
        if (result != 10) return result;
    }
}
}
int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX);
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    INITCOMMONCONTROLSEX controls{sizeof(controls), ICC_STANDARD_CLASSES}; InitCommonControlsEx(&controls);
    Run run;
    try {
        std::wstring module(32768, L'\0'); module.resize(GetModuleFileNameW(nullptr, module.data(), static_cast<DWORD>(module.size())));
        run.app = fs::path(module).parent_path();
        int count = 0; auto arguments = CommandLineToArgvW(GetCommandLineW(), &count);
        bool diagnose = false;
        for (int i = 1; i < count; ++i) {
            std::wstring arg = arguments[i];
            if (arg == L"--diagnose") { diagnose = true; run.headless = true; }
            else if (arg == L"--headless") run.headless = true;
            else if (arg == L"--report" && i + 1 < count) run.reportPath = fs::absolute(arguments[++i]);
            else if (arg == L"--timeout-seconds" && i + 1 < count) run.timeoutMs = std::clamp(std::stoull(arguments[++i]), 1ULL, 300ULL) * 1000;
            else run.arguments.push_back(arg);
        }
        LocalFree(arguments);
        try { run.session = knownFolder(FOLDERID_LocalAppData) / L"ZTMZ" / L"Startup" / uniqueId(); fs::create_directories(run.session); }
        catch (...) { run.session = fs::temp_directory_path() / (L"ZTMZ-startup-" + uniqueId()); fs::create_directories(run.session); }
        run.statusPath = run.session / L"managed-status.json";
        if (run.reportPath.empty()) run.reportPath = run.session / L"report.json";
        run.report = {{"schemaVersion", 1}, {"sessionFolder", utf8(run.session.wstring())}, {"state", "checking"}};
        run.save();
        Handle existing(OpenMutexW(SYNCHRONIZE, FALSE, mutexName));
        if (existing.value && !diagnose) {
            run.report["state"] = "already_running"; run.save();
            if (!run.headless) MessageBoxW(nullptr, run.t(L"ZTMZ is already running. Check its window or system tray icon.", L"ZTMZ 已在运行，请查看程序窗口或系统托盘图标。").c_str(), L"ZTMZ", MB_OK | MB_ICONINFORMATION);
            return 0;
        }
        return execute(run, diagnose);
    } catch (const std::exception& error) {
        const auto details = wide(error.what());
        try { recordIssue(run, {"launcher_error", L"Startup check failed", details}); } catch (...) { }
        if (!run.headless) MessageBoxW(nullptr, (details + L"\n\n" + run.reportPath.wstring()).c_str(), L"ZTMZ — Startup check failed", MB_OK | MB_ICONERROR);
        return 2;
    }
}
