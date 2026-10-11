"""Read-only DR2 countdown-state discovery. Never writes to the game process."""
import argparse
import ctypes as c
from ctypes import wintypes as w
import json
import struct
import time

p = argparse.ArgumentParser()
p.add_argument('pid', type=int)
p.add_argument('--seconds', type=int, default=1800)
p.add_argument('--output', required=True)
a = p.parse_args()
k = c.WinDLL('kernel32', use_last_error=True)
k.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
k.OpenProcess.restype = w.HANDLE
k.ReadProcessMemory.argtypes = [w.HANDLE, c.c_void_p, c.c_void_p, c.c_size_t, c.POINTER(c.c_size_t)]
k.QueryFullProcessImageNameW.argtypes = [w.HANDLE, w.DWORD, w.LPWSTR, c.POINTER(w.DWORD)]
k.CloseHandle.argtypes = [w.HANDLE]
class MBI(c.Structure):
    _fields_ = [('BaseAddress', c.c_void_p), ('AllocationBase', c.c_void_p),
                ('AllocationProtect', w.DWORD), ('PartitionId', w.WORD),
                ('RegionSize', c.c_size_t), ('State', w.DWORD),
                ('Protect', w.DWORD), ('Type', w.DWORD)]
k.VirtualQueryEx.argtypes = [w.HANDLE, c.c_void_p, c.POINTER(MBI), c.c_size_t]
k.VirtualQueryEx.restype = c.c_size_t
ps = c.WinDLL('psapi')
ps.EnumProcessModulesEx.argtypes = [w.HANDLE, c.POINTER(c.c_void_p), w.DWORD, c.POINTER(w.DWORD), w.DWORD]
h = k.OpenProcess(0x410, False, a.pid)
if not h: raise OSError(c.get_last_error())
try:
    path = c.create_unicode_buffer(32768)
    size = w.DWORD(len(path))
    if not k.QueryFullProcessImageNameW(h, 0, path, c.byref(size)) or not path.value.lower().endswith('\\dirtrally2.exe'):
        raise ValueError('Not DR2')
    modules = (c.c_void_p * 1024)()
    needed = w.DWORD()
    if not ps.EnumProcessModulesEx(h, modules, c.sizeof(modules), c.byref(needed), 3):
        raise OSError(c.get_last_error())
    base = modules[0]
    def read(address, size):
        buffer = c.create_string_buffer(size)
        count = c.c_size_t()
        if not k.ReadProcessMemory(h, address, buffer, size, c.byref(count)) or count.value != size:
            return b''
        return buffer.raw
    needle = struct.pack('<Q', base + 0x1250018)
    candidates = []
    address = 0x10000
    while address < 0x7fffffffffff:
        info = MBI()
        if not k.VirtualQueryEx(h, address, c.byref(info), c.sizeof(info)): break
        end = info.BaseAddress + info.RegionSize
        if info.State == 0x1000 and info.Type == 0x20000 and info.Protect in (4, 8, 0x40, 0x80):
            for at in range(info.BaseAddress, end, 65536):
                data = read(at, min(65536, end-at))
                offset = data.find(needle)
                while offset >= 0:
                    if (at + offset) % 8 == 0: candidates.append(at + offset)
                    offset = data.find(needle, offset+8)
        address = end
    print('COUNTDOWN CANDIDATES', [hex(v) for v in candidates], flush=True)
    previous = None
    with open(a.output, 'x', encoding='utf-8') as output:
        end = time.monotonic() + a.seconds
        while time.monotonic() < end:
            states = []
            for address in candidates:
                data = read(address, 0x70)
                if data[:8] != needle: continue
                states.append({'address': hex(address), 'flags': struct.unpack_from('<I', data, 12)[0],
                    'clock': struct.unpack_from('<f', data, 0x38)[0],
                    'interval': struct.unpack_from('<f', data, 0x3c)[0],
                    'next': struct.unpack_from('<i', data, 0x40)[0],
                    'first': struct.unpack_from('<i', data, 0x44)[0],
                    'last': struct.unpack_from('<i', data, 0x48)[0]})
            if states != previous:
                output.write(json.dumps({'time': time.time(), 'states': states}) + '\n')
                output.flush()
                print(states, flush=True)
                previous = states
            time.sleep(0.05)
finally:
    k.CloseHandle(h)
