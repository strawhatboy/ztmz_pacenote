#include "transport.hpp"
#include "dashboard_client.hpp"
#include <windows.h>
#include <winhttp.h>
#include <wincodec.h>
#include <wrl/client.h>
#include <chrono>
#include <ctime>
#include <thread>
#include <stdexcept>
namespace ztmz {
namespace {
using Microsoft::WRL::ComPtr;
constexpr std::size_t MaxMessage = 16 * 1024 * 1024;
struct Handle {
    HINTERNET value{};
    explicit Handle(HINTERNET v) : value(v) {}
    ~Handle(){if(value) WinHttpCloseHandle(value);}
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
};
struct ComScope {
    HRESULT result = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    ~ComScope(){ if(SUCCEEDED(result)) CoUninitialize(); }
};
void check(bool ok) { if(!ok) throw std::runtime_error("WinHTTP error " + std::to_string(GetLastError())); }
void checked(HRESULT result) { if(FAILED(result)) throw std::runtime_error("Image decoder failed: " + std::to_string(result)); }
void send(HINTERNET ws, const std::string& text) {
    if(WinHttpWebSocketSend(ws, WINHTTP_WEB_SOCKET_UTF8_MESSAGE_BUFFER_TYPE,
        const_cast<char*>(text.data()), static_cast<DWORD>(text.size())) != NO_ERROR)
        throw std::runtime_error("WebSocket send failed");
}
std::vector<std::uint8_t> get(HINTERNET connection, const std::string& path, std::stop_token stop) {
    // The package can name only resources on this same loopback server.
    if(path.empty() || path[0]!='/' || path.starts_with("//") || path.find('\\')!=std::string::npos)
        throw std::runtime_error("Invalid package resource path");
    auto size=MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,path.data(),static_cast<int>(path.size()),nullptr,0);
    if(size<=0) throw std::runtime_error("Invalid UTF-8 resource path");
    std::wstring wide(size,L'\0');
    MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,path.data(),static_cast<int>(path.size()),wide.data(),size);
    Handle request{WinHttpOpenRequest(connection,L"GET",wide.c_str(),nullptr,nullptr,nullptr,0)};
    check(request.value!=nullptr);
    DWORD flags=WINHTTP_DISABLE_REDIRECTS;
    check(WinHttpSetOption(request.value,WINHTTP_OPTION_DISABLE_FEATURE,&flags,sizeof(flags)));
    check(WinHttpSendRequest(request.value,nullptr,0,nullptr,0,0,0));
    check(WinHttpReceiveResponse(request.value,nullptr));
    DWORD status=0,bytes=sizeof(status);
    check(WinHttpQueryHeaders(request.value,WINHTTP_QUERY_STATUS_CODE|WINHTTP_QUERY_FLAG_NUMBER,nullptr,&status,&bytes,nullptr));
    if(status!=200) throw std::runtime_error("Resource HTTP status " + std::to_string(status));
    std::vector<std::uint8_t> result;
    for(;;) {
        if(stop.stop_requested()) throw std::runtime_error("Client stopped");
        std::uint8_t buffer[16384]; DWORD count=0;
        check(WinHttpReadData(request.value,buffer,sizeof(buffer),&count));
        if(!count) break;
        if(result.size()+count>MaxMessage) throw std::runtime_error("Resource exceeds 16 MiB");
        result.insert(result.end(),buffer,buffer+count);
    }
    return result;
}
std::shared_ptr<const ImageAsset> decode(const std::vector<std::uint8_t>& bytes) {
    ComPtr<IWICImagingFactory> factory;
    checked(CoCreateInstance(CLSID_WICImagingFactory,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&factory)));
    ComPtr<IWICStream> stream; checked(factory->CreateStream(&stream));
    checked(stream->InitializeFromMemory(const_cast<BYTE*>(bytes.data()),static_cast<DWORD>(bytes.size())));
    ComPtr<IWICBitmapDecoder> decoder;
    checked(factory->CreateDecoderFromStream(stream.Get(),nullptr,WICDecodeMetadataCacheOnLoad,&decoder));
    ComPtr<IWICBitmapFrameDecode> frame; checked(decoder->GetFrame(0,&frame));
    auto image=std::make_shared<ImageAsset>();
    checked(frame->GetSize(&image->width,&image->height));
    if(!image->width || !image->height || image->width>4096 || image->height>4096)
        throw std::runtime_error("Image dimensions exceed 4096x4096");
    ComPtr<IWICFormatConverter> converter; checked(factory->CreateFormatConverter(&converter));
    checked(converter->Initialize(frame.Get(),GUID_WICPixelFormat32bppRGBA,WICBitmapDitherTypeNone,nullptr,0,WICBitmapPaletteTypeCustom));
    image->rgba.resize(static_cast<std::size_t>(image->width)*image->height*4);
    checked(converter->CopyPixels(nullptr,image->width*4,static_cast<UINT>(image->rgba.size()),image->rgba.data()));
    return image;
}
std::string clockText() {
    auto now=std::time(nullptr); std::tm local{}; localtime_s(&local,&now);
    char buffer[16]{};std::strftime(buffer,sizeof(buffer),"%H:%M:%S",&local);return buffer;
}
}
void receive(std::stop_token stop, SharedScene& shared, unsigned short port) {
    ComScope com;
    const auto started=std::chrono::steady_clock::now();
    while (!stop.stop_requested()) {
        try {
            Handle session{WinHttpOpen(L"ZTMZ NativeDashboard/0.2", WINHTTP_ACCESS_TYPE_NO_PROXY, nullptr, nullptr, 0)};
            check(session.value != nullptr);
            check(WinHttpSetTimeouts(session.value, 2000, 2000, 2000, 1000));
            Handle connection{WinHttpConnect(session.value, L"127.0.0.1", port, 0)};
            check(connection.value != nullptr);
            Handle request{WinHttpOpenRequest(connection.value, L"GET", L"/ws", nullptr, nullptr, nullptr, 0)};
            check(request.value != nullptr);
            check(WinHttpSetOption(request.value, WINHTTP_OPTION_UPGRADE_TO_WEB_SOCKET, nullptr, 0));
            check(WinHttpSendRequest(request.value, nullptr, 0, nullptr, 0, 0, 0));
            check(WinHttpReceiveResponse(request.value, nullptr));
            Handle ws{WinHttpWebSocketCompleteUpgrade(request.value, 0)};
            check(ws.value != nullptr);
            // RECEIVE_TIMEOUT is an HTTP request option, not a WebSocket receive
            // deadline. Closing the socket cancels a pending synchronous receive.
            DWORD closeTimeout = 500;
            check(WinHttpSetOption(ws.value, WINHTTP_OPTION_WEB_SOCKET_CLOSE_TIMEOUT, &closeTimeout, sizeof(closeTimeout)));
            std::stop_callback cancelReceive(stop, [&] {
                WinHttpWebSocketClose(ws.value, WINHTTP_WEB_SOCKET_SUCCESS_CLOSE_STATUS, nullptr, 0);
            });
            auto images=std::make_shared<ImageAssets>();
            std::size_t imageBytes=0;
            DashboardClient client([&](int id) {
                auto bytes=get(connection.value,"/api/dashboard/"+std::to_string(id),stop);
                auto package=Json::parse(bytes.begin(),bytes.end());
                const auto resources=package.value("imageResources",Json::object());
                for(const auto& [key,url]:resources.items()) {
                    // Refresh assets on package/config revision; publish an immutable map.
                    auto image=decode(get(connection.value,url.get<std::string>(),stop));
                    auto old=images->find(key);
                    auto previousSize=old==images->end()?0:old->second->rgba.size();
                    if(imageBytes-previousSize+image->rgba.size()>256*1024*1024)
                        throw std::runtime_error("Dashboard image cache exceeds 256 MiB");
                    imageBytes=imageBytes-previousSize+image->rgba.size();
                    (*images)[key]=image;
                    package["imageMetadata"][key]={{"width",image->width},{"height",image->height}};
                }
                return package;
            });
            // This now advertises actual native Lua execution, using the existing wire contract.
            send(ws.value, R"({"type":"browserLuaCapabilities","supported":true})");
            { std::lock_guard lock(shared.mutex); shared.scene = {}; shared.images=std::make_shared<ImageAssets>();shared.status = "Connected; waiting for Lua packages"; }
            std::string message;
            while (!stop.stop_requested()) {
                char buffer[16384]; DWORD read{}; WINHTTP_WEB_SOCKET_BUFFER_TYPE kind;
                auto error = WinHttpWebSocketReceive(ws.value, buffer, sizeof(buffer), &read, &kind);
                if(error != NO_ERROR || kind == WINHTTP_WEB_SOCKET_CLOSE_BUFFER_TYPE) break;
                if(kind != WINHTTP_WEB_SOCKET_UTF8_FRAGMENT_BUFFER_TYPE && kind != WINHTTP_WEB_SOCKET_UTF8_MESSAGE_BUFFER_TYPE)
                    throw std::runtime_error("Expected text WebSocket message");
                message.append(buffer, read);
                if(message.size() > MaxMessage) throw std::runtime_error("Message exceeds 16 MiB");
                if(kind == WINHTTP_WEB_SOCKET_UTF8_MESSAGE_BUFFER_TYPE) {
                    auto json = Json::parse(message); message.clear();
                    auto seconds=std::chrono::duration<double>(std::chrono::steady_clock::now()-started).count();
                    auto result=client.accept(json,seconds,clockText());
                    if(!result.controlMessage.empty()) send(ws.value,result.controlMessage);
                    if(result.changed) {
                        // Compatibility layers in hybrid scenes can reference images from
                        // dashboards that were not downloaded as Lua packages.
                        for(const auto& layer:client.scene().layers) for(const auto& command:layer.commands) {
                            if(!command.contains("imageKey") || !command.contains("imageUrl")) continue;
                            auto key=command.at("imageKey").get<std::string>();
                            if(images->contains(key)) continue;
                            auto image=decode(get(connection.value,command.at("imageUrl").get<std::string>(),stop));
                            if(imageBytes+image->rgba.size()>256*1024*1024) throw std::runtime_error("Image cache full");
                            imageBytes+=image->rgba.size();(*images)[key]=std::move(image);
                        }
                        std::lock_guard lock(shared.mutex);
                        shared.scene=client.scene();shared.status=client.status();
                        shared.images=std::make_shared<ImageAssets>(*images);
                    }
                }
            }
            { std::lock_guard lock(shared.mutex); shared.status="Disconnected; reconnecting"; }
        } catch(const std::exception& e) {
            std::lock_guard lock(shared.mutex); shared.status = std::string("Disconnected: ") + e.what();
        }
        { std::lock_guard lock(shared.mutex); shared.scene = {}; shared.images=std::make_shared<ImageAssets>(); }
        for(int i=0; i<10 && !stop.stop_requested(); ++i) std::this_thread::sleep_for(std::chrono::milliseconds(100));
    }
}
}
