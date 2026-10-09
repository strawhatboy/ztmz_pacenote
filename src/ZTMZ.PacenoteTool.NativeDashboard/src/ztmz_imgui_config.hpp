#pragma once
// Atlas construction uses ImGui allocation helpers. Even those helpers consult
// the current context for debug bookkeeping, so workers must not share it with
// the game's rendering thread. All ImGui consumers inherit this configuration.
struct ImGuiContext;
extern thread_local ImGuiContext* ZtmzImGuiContext;
#define GImGui ZtmzImGuiContext
