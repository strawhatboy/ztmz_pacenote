#include "startup_core.h"
#include <windows.h>
#include <shellapi.h>
#include <fstream>
#include <iostream>
#include <thread>

using namespace startup;
int checks = 0;
void check(bool condition, const char* message) { ++checks; if (!condition) throw std::runtime_error(message); }
template<class F> void rejects(F action, const char* message) {
    bool rejected = false; try { action(); } catch (const std::exception&) { rejected = true; }
    check(rejected, message);
}
void bytes(const fs::path& file, const std::string& value) { std::ofstream(file, std::ios::binary) << value; }
std::string contents(const fs::path& file) { std::ifstream stream(file, std::ios::binary); return {std::istreambuf_iterator<char>(stream), {}}; }
int main() {
    auto root = fs::temp_directory_path() / (L"ztmz-launcher-tests-" + std::to_wstring(GetCurrentProcessId()));
    try {
        fs::create_directories(root / L"games");
        auto selected = resolveData((root / L"missing-env").wstring(), (root / L"registry").wstring(), root);
        check(selected.source == "environment" && selected.path.filename() == L"missing-env", "environment precedence");
        rejects([&] { checkWritable(selected.path); }, "missing configured folder must fail");
        check(!fs::exists(selected.path), "must not create empty profile");
        check(resolveData(L" \t", root.wstring(), L"C:\\ignored").path == root, "registry precedence");
        check(resolveData(L"", L"", root).path == root / L"My Games" / L"ZTMZClub_nextgen", "default path");
        checkWritable(root);
        std::vector<std::wstring> args{L"", L"C:\\路书 工具\\", L"a\"b", L"end\\\\", L"a\\\" b"};
        std::wstring command = L"launcher.exe";
        for (const auto& arg : args) command += L" " + quote(arg);
        int count; auto parsed = CommandLineToArgvW(command.c_str(), &count);
        check(count == args.size() + 1, "argument count");
        for (size_t i = 0; i < args.size(); ++i) check(args[i] == parsed[i + 1], "Windows argument round trip");
        LocalFree(parsed);
        auto config = root / L"userconfig.json";
        check(validConfiguration(config, false), "absent override is valid");
        bytes(config, "{broken");
        rejects([&] { validConfiguration(config, false); }, "invalid JSON must fail");
        check(contents(config) == "{broken", "validation cannot rewrite settings");
        bytes(config, "[]"); check(!validConfiguration(config, false), "array is not settings");
        bytes(config, "null"); check(!validConfiguration(config, false), "null is not settings");
        bytes(config, std::string("\xff\xfe{\0}\0", 6));
        check(validConfiguration(config, false), "UTF-16 LE configuration");
        bytes(config, std::string("\xfe\xff\0{\0}", 6));
        check(validConfiguration(config, false), "UTF-16 BE configuration");
        bytes(config, "{/* comment */\"Language\":\"zh-cn\"}");
        check(validConfiguration(config, true), "base config permits comments");
        rejects([&] { validConfiguration(config, false); }, "user config rejects comments like managed parser");
        const auto original = contents(config);
        auto backup = backupConfiguration(config, root, L"123-456");
        check(!fs::exists(config) && contents(backup) == original, "backup preserves original bytes");
        bytes(config, "{new}");
        rejects([&] { backupConfiguration(config, root, L"123-456"); }, "backup collision must fail");
        check(contents(config) == "{new}" && contents(backup) == original, "collision preserves both files");
        bytes(root / L"data.zdb", "database");
        rejects([&] { backupConfiguration(root / L"data.zdb", root, L"123"); }, "never reset database");
        rejects([&] { backupConfiguration(config, root, L"../escape"); }, "reject suffix traversal");
        bytes(root / L"games" / L"rbr.json", "{}");
        check(fs::exists(backupConfiguration(root / L"games" / L"rbr.json", root, L"123")), "game config backup");
        auto missing = missingFiles(root, Json::array({"data.zdb", "missing.dll"}));
        check(missing.size() == 1 && missing[0].filename() == L"missing.dll", "report missing required files");
        for (const auto& bad : {"../escape", "C:/escape", "/escape"})
            rejects([&] { missingFiles(root, Json::array({bad})); }, "reject unsafe manifest path");
        auto status = root / L"status.json";
        writeJson(status, {{"state", "starting"}}); writeJson(status, {{"state", "ready"}});
        check(readJson(status)["state"] == "ready", "atomic status replacement");
        // Reproduce a report reader that temporarily denies replacement.
        auto blocker = CreateFileW(status.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr, OPEN_EXISTING, 0, nullptr);
        check(blocker != INVALID_HANDLE_VALUE, "open report lock fixture");
        std::exception_ptr failure;
        std::thread writer([&] { try { writeJson(status, {{"state", "retry"}}); } catch (...) { failure = std::current_exception(); } });
        Sleep(75);
        CloseHandle(blocker);
        writer.join();
        if (failure) std::rethrow_exception(failure);
        check(readJson(status)["state"] == "retry", "recover from transient report replacement lock");
        check(decide(Json::object(), false, 1, 100) == Decision::Waiting, "wait for handshake");
        check(decide({{"state", "ready"}}, false, 100, 100) == Decision::Ready, "ready beats timeout");
        check(decide({{"state", "ready"}}, true, 1, 100) == Decision::Exited, "exit beats stale ready");
        check(decide({{"state", "failed"}}, true, 1, 100) == Decision::Failed, "preserve failure report after exit");
        check(decide({{"state", "already_running"}}, true, 1, 100) == Decision::Ready, "duplicate instance is not a crash");
        check(decide(Json::object(), false, 100, 100) == Decision::Slow, "timeout is not an automatic kill");
        fs::remove_all(root);
        std::cout << checks << " startup policy checks passed\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << error.what() << " (fixtures kept at " << utf8(root.wstring()) << ")\n";
        return 1;
    }
}
