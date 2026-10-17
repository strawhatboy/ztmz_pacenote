"""Exercise the 2025 Lua dashboard through the existing native Lua test runner.

Uses the relevant ACR telemetry captured at 2026-10-17 06:03:06 UTC. The capture
itself succeeds; changing only the engine RPM/limit reproduces the startup defect.
No native or .NET build is performed. Optional --package replays deployed Lua.
"""
import argparse
import json
from pathlib import Path
import struct
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
WEB = ROOT / "src/ZTMZ.PacenoteTool.WebDashboard"
DASH = WEB / "dashboards/modern_wrc_2025"
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--runner", type=Path, default=ROOT / "build/native-dashboard/production/Release/lua_tests.exe")
parser.add_argument("--package", type=Path, help="Optional captured dashboard package instead of current source")
args = parser.parse_args()

if args.package:
    package = json.loads(args.package.read_text(encoding="utf-8-sig"))
else:
    settings = json.loads((DASH / "settings.json").read_text(encoding="utf-8-sig"))
    package = {
        "id": 12,
        "luaScript": (DASH / "script.lua").read_text(encoding="utf-8-sig"),
        "commonScripts": [p.read_text(encoding="utf-8-sig") for p in sorted((WEB / "dashboards/common").glob("*.lua"))],
        "settings": dict(zip(settings["PropertyName"], settings["PropertyValue"])),
        "imageResources": {}, "imageMetadata": {},
    }
    for image in (DASH / "images").glob("*.png"):
        key = "12:images@" + image.stem
        package["imageResources"][key] = "/test/" + image.name
        width, height = struct.unpack(">II", image.read_bytes()[16:24])
        package["imageMetadata"][key] = {"width": width, "height": height}

# Disable blinking to inspect the actual RPM clip on every test frame.
package["settings"]["dashboards.settings.showRPMBlink"] = False
captured = """{Brake=0,Clutch=1,CompletionRate=.36216024,Gear=0,
    G_lat=-.00013301535,G_long=-.00004108617,HandBrake=0,HandBrakeValid=true,
    LapDistance=4374.152,LapTime=412.168,MaxGears=5,MaxRPM=7500,RPM=1884,
    ShiftLightsRPMEnd=7300,ShiftLightsRPMStart=6700,ShiftLightsRPMValid=true,
    Speed=.14453623,Steering=.0008955871,Throttle=0,TrackLength=12077.946,Time=412.168}"""
cases = [
    ("captured", "", 1884 / 7500),
    ("zero-limit", "data.MaxRPM=0", 0),
    ("startup", "data.RPM=0;data.MaxRPM=0", 0),
    ("missing-limit", "data.MaxRPM=nil", 0),
    ("negative-limit", "data.MaxRPM=-1", 0),
    ("nonfinite-limit", "data.MaxRPM=math.huge", 0),
    ("nan-limit", "data.MaxRPM=0/0", 0),
    ("missing-rpm", "data.RPM=nil", 0),
    ("nonfinite-rpm", "data.RPM=math.huge", 0),
    ("nan-rpm", "data.RPM=0/0", 0),
    ("negative-rpm", "data.RPM=-1", 0),
    ("over-limit", "data.RPM=9000;data.MaxRPM=7500", 1),
    ("half-limit", "data.RPM=3750", .5),
]
failures = []
with tempfile.TemporaryDirectory(prefix="ztmz-wrc2025-rpm-") as directory:
    for name, override, expected in cases:
        test = dict(package)
        test["testName"] = "wrc2025-rpm-" + name
        test["luaScript"] += "\nlocal originalUpdate=onUpdate\nfunction onUpdate(a)\nlocal data=" + captured + "\n" + override + "\n" + """
            a.GameData=data
            a.GameContext={GameState=3,LocalReplayValid=false}
            a.Graphics.Width=1280;a.Graphics.Height=720
            local gfx=a.Graphics
            local clip,image=gfx.ClipRegionStart,gfx.DrawImage
            local bounds=nil
            local rpmSeen=false
            gfx.ClipRegionStart=function(l,t,r,b)
                bounds={l,t,r,b}
                clip(l,t,r,b)
            end
            gfx.DrawImage=function(img,l,t,r,b,opacity)
                if img==a.Self.ImageResources['images@rpm'] then
                    rpmSeen=true
                    assert(bounds~=nil,'RPM image must be clipped')
                    for _,n in ipairs(bounds) do assert(n-n==0,'Non-finite RPM clip coordinate') end
                    assert(bounds[1]>=l and bounds[3]<=r and bounds[3]>=bounds[1],'RPM clip exceeds image')
                    local fraction=(bounds[3]-bounds[1])/(r-l)
                    assert(math.abs(fraction-EXPECTED)<.000001,'Incorrect RPM fill fraction')
                end
                image(img,l,t,r,b,opacity)
            end
            originalUpdate(a)
            assert(rpmSeen,'RPM image was not inspected')
            gfx.ClipRegionStart=clip;gfx.DrawImage=image
        end
        """.replace("EXPECTED", repr(expected))
        path = Path(directory) / (name + ".json")
        path.write_text(json.dumps(test), encoding="utf-8")
        result = subprocess.run([str(args.runner.resolve()), str(path)], capture_output=True, text=True, timeout=15)
        if result.returncode:
            failures.append(name)
            print(f"FAIL {name}: {result.stderr.strip() or result.stdout.strip()}")
        else:
            print(f"PASS {name}")
if failures:
    raise SystemExit("Failed RPM cases: " + ", ".join(failures))
print(f"Native WRC 2025 RPM regression passed ({len(cases)} cases).")
