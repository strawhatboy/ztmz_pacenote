#pragma once
#include <functional>
#include <string>
#include <string_view>
namespace ztmz {
// Process-lifetime server: caller must pin its DLL before starting it.
// One UTF-8 JSON request/response per connection, each bounded to 4096 bytes.
void startControlPipe(std::function<std::string(std::string_view)> handler);
std::string controlInstance();
}
