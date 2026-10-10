"""Bounded read-only WRCG session inspection; never writes or injects."""
import argparse
import ctypes as c
import struct
from ctypes import wintypes as w
parser = argparse.ArgumentParser()
parser.add_argument("pid", type=int)
parser.add_argument("base", type=lambda x:int(x,0))
args = parser.parse_args()
k = c.WinDLL("kernel32",use_last_error=True)
k.OpenProcess.argtypes=[w.DWORD,w.BOOL,w.DWORD]; k.OpenProcess.restype=w.HANDLE
k.ReadProcessMemory.argtypes=[w.HANDLE,c.c_void_p,c.c_void_p,c.c_size_t,c.POINTER(c.c_size_t)]
k.QueryFullProcessImageNameW.argtypes=[w.HANDLE,w.DWORD,w.LPWSTR,c.POINTER(w.DWORD)]
k.CloseHandle.argtypes=[w.HANDLE]
h=k.OpenProcess(0x1010,False,args.pid)
if not h: raise OSError(c.get_last_error())
try:
    path=c.create_unicode_buffer(32768); n=w.DWORD(len(path))
    if not k.QueryFullProcessImageNameW(h,0,path,c.byref(n)) or not path.value.lower().endswith("\\wrcg.exe"):
        raise ValueError("Not WRCG")
    def read(p,n):
        if p<65536 or p>0x7fffffffffff:return b""
        b=c.create_string_buffer(n); count=c.c_size_t()
        return b.raw[:count.value] if k.ReadProcessMemory(h,p,b,n,c.byref(count)) else b""
    def ptr(p):
        b=read(p,8); return struct.unpack("<Q",b)[0] if len(b)==8 else 0
    def dump(name,p,size):
        print(name,hex(p)); data=read(p,size)
        for i in range(0,len(data),8):
            v=struct.unpack_from("<Q",data,i)[0]
            if not v:continue
            b=read(v,128); text=b.split(b"\0")[0]
            label=repr(text) if len(text)>2 and all(32<=t<127 for t in text) else ""
            print(f"  +{i:03x}: {v:016x} {label}")
    manager=ptr(args.base+0x1c61660)
    print("manager",hex(manager))
    for off in [0xd48,0xd50,0xd58]:dump(f"manager+{off:x}",ptr(manager+off),0x2a8)
    index=read(manager+0x1380,1)
    if index:
        profile=ptr(manager+0x1378)+index[0]*0x210
        dump("profile",profile,0x210)
        for off in [0x20,0x30,0x38,0x40,0x48]:dump(f"profile+{off:x}",ptr(profile+off),0x200)
finally:k.CloseHandle(h)
