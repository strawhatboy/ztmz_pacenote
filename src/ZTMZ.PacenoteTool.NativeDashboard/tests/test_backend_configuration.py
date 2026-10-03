"""Unavailable backends must fail configuration before downloading dependencies."""
import pathlib
import subprocess
import sys
import tempfile
cmake, source = sys.argv[1:]
with tempfile.TemporaryDirectory(prefix="ztmz-backend-config-") as root:
    cases = []
    cases.append(("typo", "Unknown graphics backend"))
    if sys.platform != "win32":
        cases.extend((api, "requires Windows") for api in ("dx9", "dx10", "dx11", "dx12", "vulkan"))
    for api, expected in cases:
        result = subprocess.run([cmake, "-S", source, "-B", str(pathlib.Path(root) / api),
            "-DZTMZ_RENDER_BACKENDS=" + api], capture_output=True, text=True, timeout=30)
        output = result.stdout + result.stderr
        if result.returncode == 0 or expected not in output:
            raise SystemExit(f"Unexpected configure result for {api}:\n{output}")
print("Unsupported/unknown backend configurations rejected correctly")
