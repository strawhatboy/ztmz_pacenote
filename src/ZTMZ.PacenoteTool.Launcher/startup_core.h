#pragma once
#include <filesystem>
#include <optional>
#include <string>
#include <vector>
#include <nlohmann/json.hpp>

namespace startup {
namespace fs = std::filesystem;
using Json = nlohmann::json;
std::string utf8(const std::wstring& value);
std::wstring wide(const std::string& value);
std::wstring quote(const std::wstring& argument);
Json readJson(const fs::path& path, bool comments = false);
void writeJson(const fs::path& path, const Json& value);
struct DataLocation { fs::path path; std::string source; };
DataLocation resolveData(const std::wstring& environment, const std::wstring& registry, const fs::path& documents);
std::vector<fs::path> missingFiles(const fs::path& root, const Json& relatives);
void checkWritable(const fs::path& directory);
bool validConfiguration(const fs::path& path, bool comments);
fs::path backupConfiguration(const fs::path& path, const fs::path& dataRoot, const std::wstring& suffix);
enum class Decision { Waiting, Ready, Failed, Exited, Slow };
Decision decide(const Json& status, bool exited, unsigned long long elapsedMs, unsigned long long timeoutMs);
}
