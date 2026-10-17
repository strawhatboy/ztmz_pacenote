#include "startup_core.h"
#include <windows.h>
#include <fstream>
#include <memory>
#include <stdexcept>

namespace startup {
std::string utf8(const std::wstring& value) {
    if (value.empty()) return {};
    int count = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (!count) throw std::runtime_error("Invalid Unicode path or text");
    std::string result(count, '\0');
    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), result.data(), count, nullptr, nullptr);
    return result;
}
std::wstring wide(const std::string& value) {
    if (value.empty()) return {};
    int count = MultiByteToWideChar(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), nullptr, 0);
    if (!count) throw std::runtime_error("Invalid UTF-8 text");
    std::wstring result(count, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), result.data(), count);
    return result;
}
std::wstring quote(const std::wstring& argument) {
    std::wstring result = L"\"";
    size_t slashes = 0;
    for (auto c : argument) {
        if (c == L'\\') { ++slashes; continue; }
        result.append(c == L'"' ? slashes * 2 + 1 : slashes, L'\\');
        result += c;
        slashes = 0;
    }
    result.append(slashes * 2, L'\\');
    return result + L'"';
}
Json readJson(const fs::path& path, bool comments) {
    // Share delete so polling cannot prevent the managed writer's atomic rename.
    std::unique_ptr<void, decltype(&CloseHandle)> input(CreateFileW(path.c_str(), GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr), CloseHandle);
    if (input.get() == INVALID_HANDLE_VALUE) throw std::runtime_error("Cannot read JSON file");
    LARGE_INTEGER size{};
    if (!GetFileSizeEx(input.get(), &size) || size.QuadPart > 4 * 1024 * 1024) throw std::runtime_error("JSON file exceeds 4 MB or is unreadable");
    std::string bytes(static_cast<size_t>(size.QuadPart), '\0');
    DWORD count = 0;
    if (!ReadFile(input.get(), bytes.data(), static_cast<DWORD>(bytes.size()), &count, nullptr) || count != bytes.size())
        throw std::runtime_error("Cannot read complete JSON file");
    // Match File.ReadAllText's UTF-16 BOM handling for settings saved by editors.
    if (bytes.size() >= 2 && ((bytes[0] == '\xFF' && bytes[1] == '\xFE') || (bytes[0] == '\xFE' && bytes[1] == '\xFF'))) {
        if (bytes.size() % 2) throw std::runtime_error("Incomplete UTF-16 JSON file");
        const bool little = bytes[0] == '\xFF';
        std::u16string text;
        for (size_t i = 2; i < bytes.size(); i += 2) {
            auto first = static_cast<unsigned char>(bytes[i]), second = static_cast<unsigned char>(bytes[i + 1]);
            text += static_cast<char16_t>(little ? first | (second << 8) : (first << 8) | second);
        }
        return Json::parse(text, nullptr, true, comments);
    }
    return Json::parse(bytes, nullptr, true, comments);
}
void writeJson(const fs::path& path, const Json& value) {
    auto temp = path; temp += L".new";
    {
        std::ofstream output(temp, std::ios::binary | std::ios::trunc);
        output << value.dump(2);
        output.flush();
        if (!output) throw std::runtime_error("Cannot save startup report");
    }
    for (int attempt = 0; ; ++attempt) {
        if (MoveFileExW(temp.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) break;
        const auto error = GetLastError();
        if (attempt >= 10 || (error != ERROR_SHARING_VIOLATION && error != ERROR_ACCESS_DENIED))
            throw std::runtime_error("Cannot replace startup report (Windows error " + std::to_string(error) + ")");
        Sleep(25);
    }
}
DataLocation resolveData(const std::wstring& environment, const std::wstring& registry, const fs::path& documents) {
    auto nonblank = [](const std::wstring& text) { return text.find_first_not_of(L" \t\r\n") != std::wstring::npos; };
    DataLocation result;
    if (nonblank(environment)) result = {environment, "environment"};
    else if (nonblank(registry)) result = {registry, "registry"};
    else result = {documents / L"My Games" / L"ZTMZClub_nextgen", "default"};
    result.path = fs::absolute(result.path).lexically_normal();
    return result;
}
std::vector<fs::path> missingFiles(const fs::path& root, const Json& relatives) {
    if (!relatives.is_array()) throw std::runtime_error("Invalid startup manifest file list");
    std::vector<fs::path> missing;
    for (const auto& item : relatives) {
        fs::path relative(wide(item.get<std::string>()));
        if (relative.empty() || relative.has_root_path() || relative.native().find(L':') != std::wstring::npos) throw std::runtime_error("Unsafe startup manifest path");
        for (const auto& part : relative) if (part == L"..") throw std::runtime_error("Unsafe startup manifest path");
        auto path = root / relative;
        if (!fs::is_regular_file(path) || fs::file_size(path) == 0) missing.push_back(path);
    }
    return missing;
}
void checkWritable(const fs::path& directory) {
    // A stale configured folder must never silently create a new empty profile.
    if (!fs::is_directory(directory)) throw std::runtime_error("Data directory does not exist");
    auto probe = directory / (L".ztmz-write-check-" + std::to_wstring(GetCurrentProcessId()) + L"-" + std::to_wstring(GetTickCount64()));
    HANDLE file = CreateFileW(probe.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW,
        FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_DELETE_ON_CLOSE, nullptr);
    if (file == INVALID_HANDLE_VALUE) throw std::runtime_error("Data directory is not writable (Windows error " + std::to_string(GetLastError()) + ")");
    CloseHandle(file);
}
bool validConfiguration(const fs::path& path, bool comments) {
    if (!fs::exists(path)) return true; // the application creates defaults
    return readJson(path, comments).is_object();
}
fs::path backupConfiguration(const fs::path& path, const fs::path& dataRoot, const std::wstring& suffix) {
    const auto root = fs::weakly_canonical(dataRoot);
    const auto parent = fs::weakly_canonical(path.parent_path());
    const auto name = path.filename();
    bool allowed = (parent == root && (name == L"config.json" || name == L"userconfig.json")) ||
        (parent == root / L"games" && path.extension() == L".json");
    if (!allowed || fs::is_symlink(path) || (GetFileAttributesW(path.c_str()) & FILE_ATTRIBUTE_REPARSE_POINT))
        throw std::runtime_error("Refusing to reset a file outside the configuration allowlist");
    if (suffix.empty() || suffix.find_first_not_of(L"0123456789-") != std::wstring::npos)
        throw std::runtime_error("Invalid configuration backup suffix");
    auto backup = path; backup += L".startup-backup-" + suffix;
    // An atomic rename preserves every byte and never overwrites an older backup.
    if (!MoveFileExW(path.c_str(), backup.c_str(), MOVEFILE_WRITE_THROUGH))
        throw std::runtime_error("Could not back up settings; the original file was not reset (Windows error " + std::to_string(GetLastError()) + ")");
    return backup;
}
Decision decide(const Json& status, bool exited, unsigned long long elapsedMs, unsigned long long timeoutMs) {
    const auto state = status.value("state", std::string{});
    if (state == "failed") return Decision::Failed;
    if (state == "already_running") return Decision::Ready;
    if (exited) return Decision::Exited; // exit beats a stale ready marker
    if (state == "ready") return Decision::Ready;
    if (elapsedMs >= timeoutMs) return Decision::Slow;
    return Decision::Waiting;
}
}
