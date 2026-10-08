#include "transport.hpp"
#include <condition_variable>
// Hook tests keep the real start/stop/join path, but have no dependency on a
// running ZTMZ server. Drawing fixtures are supplied by the test-only DLL export.
namespace ztmz {
void receive(std::stop_token stop,SharedScene&,unsigned short) {
    std::mutex mutex;std::condition_variable_any changed;
    std::unique_lock lock(mutex);changed.wait(lock,stop,[]{return false;});
}
}
