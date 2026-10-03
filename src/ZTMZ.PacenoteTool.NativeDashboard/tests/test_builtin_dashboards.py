"""Execute repository scripts unchanged through the compiled native Lua host."""
import json
import pathlib
import re
import struct
import subprocess
import sys
import tempfile

binary, web = pathlib.Path(sys.argv[1]).resolve(), pathlib.Path(sys.argv[2]).resolve()
def read_json(path):
    # Repository descriptors accept JSON trailing commas.
    return json.loads(re.sub(r",\s*([}\]])", r"\1", path.read_text(encoding="utf-8-sig")))

def image_dimensions(path):
    data = path.read_bytes()
    if data.startswith(b"\x89PNG\r\n\x1a\n"):
        return dict(zip(("width", "height"), struct.unpack(">II", data[16:24])))
    return {"width": 256, "height": 256}

failures=[]
with tempfile.TemporaryDirectory(prefix="ztmz-lua-tests-") as temp:
    for dashboard in sorted((web/"dashboards").iterdir()):
        if not (dashboard/"script.lua").exists():
            continue
        info=read_json(dashboard/"info.json")
        if not info.get("SupportsWebDashboardClientLua", False):
            continue
        settings=read_json(dashboard/"settings.json")
        p={"id": 0, "testName": dashboard.name,
           "luaScript": (dashboard/"script.lua").read_text(),
           "commonScripts": [s.read_text() for s in sorted((web/"dashboards/common").glob("*.lua"))],
           "settings": dict(zip(settings["PropertyName"], settings["PropertyValue"])),
           "imageResources": {}, "imageMetadata": {}}
        def add_image(key,path):
            p["imageResources"]["0:"+key]="/test/"+path.name
            p["imageMetadata"]["0:"+key]=image_dimensions(path)
        for key,value in info.get("ImageResources",{}).items():
            add_image(key,dashboard/value["Path"])
        for key,value in info.get("ImageResourcesInDirectory",{}).items():
            for image in (dashboard/value["Path"]).rglob("*.png"):
                add_image(key+"@"+image.stem,image)
        if info.get("UsesPacenoteIcons"):
            p["pacenoteIconTokenMap"]={"3_left":"3_left","long":"long"}
            for token in ("3_left","long"):
                add_image("pacenote@"+token,web/"pacenote-icons/v1/icons"/(token+".png"))
        path=pathlib.Path(temp)/(dashboard.name+".json")
        path.write_text(json.dumps(p))
        result=subprocess.run([str(binary),str(path)],capture_output=True,text=True,timeout=10)
        print(result.stdout,end="")
        if result.returncode:
            failures.append(dashboard.name)
            print(dashboard.name+": "+result.stderr,file=sys.stderr)
if failures:
    raise SystemExit("Failed dashboards: "+", ".join(failures))
