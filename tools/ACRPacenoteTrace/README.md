# ACR Pacenote Trace

## Setup

The MinHook dependency is a Git submodule. Before building any helper, initialize it:

```bat
git submodule update --init --recursive
```

Development-only DLL for locating the ACR Unreal pacenote dispatch path.

It hooks the Dumper-7-generated `UObject::ProcessEvent` offset for the exact
game build and appends only calls whose object or function name contains
`Pacenote` to `tmp/acr_pacenote_trace.log`. It does not block or alter calls.

The DLL is intentionally not included in application packaging. It must be
rebuilt after an ACR update and should only be used in an offline session.
