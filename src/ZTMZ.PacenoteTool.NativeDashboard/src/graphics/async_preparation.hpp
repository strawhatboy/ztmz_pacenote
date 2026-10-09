#pragma once
#include <atomic>
#include <exception>
#include <memory>
#include <thread>
#include <utility>
namespace ztmz {
// A worker exclusively owns the result until publication. Polling never waits.
// COM-backed jobs may be abandoned; raw Vulkan devices must drain before destroy.
template<class T> class AsyncPreparation {
    struct State {
        std::atomic<bool> done{false};
        std::unique_ptr<T> result;
        std::exception_ptr error;
    };
    std::shared_ptr<State> state_;
public:
    bool started() const {return bool(state_);}
    template<class Factory> void start(Factory factory) {
        auto state=std::make_shared<State>();
        std::thread([state,factory=std::move(factory)]() mutable {
            try {state->result=factory();}catch(...) {state->error=std::current_exception();}
            state->done.store(true,std::memory_order_release);
            state->done.notify_all();
        }).detach();
        state_=std::move(state);
    }
    std::unique_ptr<T> take() {
        if(!state_ || !state_->done.load(std::memory_order_acquire))return {};
        if(state_->error)std::rethrow_exception(state_->error);
        return std::move(state_->result);
    }
    void cancel() {state_.reset();}
    void drain() noexcept {
        if(!state_)return;
        state_->done.wait(false,std::memory_order_acquire);
        // Destroy resources here, before the caller destroys a borrowed device.
        state_->result.reset();state_.reset();
    }
};
}
