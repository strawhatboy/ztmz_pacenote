"""Read-only inspection of the verified DR2 process; no game memory writes."""
import argparse
import ctypes as c
from ctypes import wintypes as w
import struct
import re

p = argparse.ArgumentParser()
p.add_argument('pid', type=int)
p.add_argument('--address', type=lambda x: int(x, 0))
p.add_argument('--size', type=lambda x: int(x, 0), default=256)
p.add_argument('--strings', action='store_true')
a = p.parse_args()
k = c.WinDLL('kernel32', use_last_error=True)
k.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
k.OpenProcess.restype = w.HANDLE
k.ReadProcessMemory.argtypes = [w.HANDLE, c.c_void_p, c.c_void_p, c.c_size_t, c.POINTER(c.c_size_t)]
k.QueryFullProcessImageNameW.argtypes = [w.HANDLE, w.DWORD, w.LPWSTR, c.POINTER(w.DWORD)]
k.CloseHandle.argtypes = [w.HANDLE]
ps = c.WinDLL('psapi')
ps.EnumProcessModulesEx.argtypes = [w.HANDLE, c.POINTER(c.c_void_p), w.DWORD, c.POINTER(w.DWORD), w.DWORD]
h = k.OpenProcess(0x410, False, a.pid)
if not h: raise OSError(c.get_last_error())
try:
    path = c.create_unicode_buffer(32768)
    length = w.DWORD(len(path))
    if not k.QueryFullProcessImageNameW(h, 0, path, c.byref(length)) or not path.value.lower().endswith('\\dirtrally2.exe'):
        raise ValueError('Not DR2')
    modules = (c.c_void_p * 1024)()
    needed = w.DWORD()
    if not ps.EnumProcessModulesEx(h, modules, c.sizeof(modules), c.byref(needed), 3): raise OSError(c.get_last_error())
    base = modules[0]
    print('BASE', hex(base))
    def read(address, size):
        if not 65536 <= address <= 0x7fffffffffff or not 0 < size <= 65536: raise ValueError('Invalid read')
        buffer = c.create_string_buffer(size)
        count = c.c_size_t()
        if not k.ReadProcessMemory(h, address, buffer, size, c.byref(count)) or count.value != size:
            raise OSError(f'Read failed at {address:x}: {c.get_last_error()}')
        return buffer.raw
    def ptr(address): return struct.unpack('<Q', read(address, 8))[0]
    def dump(address, size):
        data = read(address, size)
        for offset in range(0, len(data) - 7, 8):
            value = struct.unpack_from('<Q', data, offset)[0]
            if value: print(f'+{offset:04x} {value:016x} floats={struct.unpack_from("<ff", data, offset)}')
    if a.address:
        if a.strings:
            for match in re.finditer(rb'[ -~]{3,}', read(a.address, a.size)):
                print(hex(match.start()), match.group().decode('ascii'))
        else:
            dump(a.address, a.size)
    else:
        count = struct.unpack('<i', read(base + 0x201b930, 4))[0]
        print('OBJECT COUNT', count)
        if 0 < count <= 32:
            for i in range(count):
                obj = ptr(base + 0x201b7b0 + i*8)
                print('OBJECT', i, hex(obj), 'vtable', hex(ptr(obj)-base))
                physics = ptr(obj + 8)
                print('PHYSICS', hex(physics), 'rpm', struct.unpack('<f',read(physics+0x13d8,4))[0])
                print('CONTROLS', struct.unpack('<11f', read(physics+0x380,44)))
                print('RPM LIMITS', [struct.unpack('<f',struct.pack('<I',(struct.unpack('<I',read(physics+o,4))[0]*m)&0xffffffff))[0] for o,m in [(0x6d0,0xb05c6c43),(0x6d4,0x6fa85a41)]])
                print('SHIFT WARNING',struct.unpack('<f',struct.pack('<I',(struct.unpack('<I',read(physics+0x804,4))[0]*0xb0ef7c77)&0xffffffff))[0], 'HANDBRAKE',struct.unpack('<f',read(physics+0x390,4))[0])
                damage = ptr(physics + 0x2bd0)
                print('DAMAGE', hex(damage), 'MODE', struct.unpack('<i',read(obj+0xd48,4))[0])
                print('DAMAGE VALUES', [(hex(o), struct.unpack('<f',read(damage+8+o,4))[0]) for o in range(0,0xb0,4)])
                if struct.unpack('<i', read(obj+0xd48,4))[0] == 0:
                    print('TYRE STATES', list(read(damage+0x120,4)))
                    parameters = ptr(damage + 0x440)
                    print('DAMAGE THRESHOLDS', [(hex(o), struct.unpack('<f',read(parameters+o,4))[0]) for o in [0x84,0xb8,0xc0,0xec,0xf4,0xfc,0x104,0x18c]])
                    for wheel in range(4):
                        print('WHEEL', wheel)
                        dump(physics+0x1480+wheel*0x420,0x80)
                dump(obj + 0xc80, 0x100)
finally:
    k.CloseHandle(h)
