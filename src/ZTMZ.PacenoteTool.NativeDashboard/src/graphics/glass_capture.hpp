#pragma once
namespace ztmz {
// Per-submission diagnostics; no GPU queries or scene readback in production.
struct GlassCaptureStats {
    unsigned copies=0, width=0, height=0;
    bool multisampleResolve=false;
};
}
