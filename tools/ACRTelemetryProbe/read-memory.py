"""Read-only, bounded ACR telemetry inspection. Never writes game memory."""
import argparse
import ctypes as c
from ctypes import wintypes as w
import struct
import time

p = argparse.ArgumentParser()
p.add_argument('pid', type=int)
p.add_argument('--address', type=lambda x: int(x, 0))
p.add_argument('--size', type=lambda x: int(x, 0), default=256)
p.add_argument('--controls', action='store_true')
p.add_argument('--simulation', action='store_true')
p.add_argument('--damage', action='store_true')
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
    if not k.QueryFullProcessImageNameW(h, 0, path, c.byref(length)) or not path.value.lower().endswith('\\acr.exe'):
        raise ValueError('Not ACR')
    modules = (c.c_void_p * 1024)()
    needed = w.DWORD()
    if not ps.EnumProcessModulesEx(h, modules, c.sizeof(modules), c.byref(needed), 3):
        raise OSError(c.get_last_error())
    base = modules[0]
    print('BASE', hex(base))
    def read(address, size):
        if not 65536 <= address <= 0x7fffffffffff or size > 65536: raise ValueError('Invalid read')
        buffer = c.create_string_buffer(size)
        count = c.c_size_t()
        if not k.ReadProcessMemory(h, address, buffer, size, c.byref(count)) or count.value != size:
            raise OSError(f'Read failed at {address:x}: {c.get_last_error()}')
        return buffer.raw
    def ptr(address): return struct.unpack('<Q', read(address, 8))[0]
    def name(address):
        index, number = struct.unpack('<II', read(address, 8))
        block = ptr(base + 0xb27ba80 + 0x10 + (index >> 16) * 8)
        entry = block + (index & 65535) * 2
        header = struct.unpack('<H', read(entry, 2))[0]
        length = header >> 6
        if not 0 < length < 1024: return f'invalid-name:{index:x}/{header:x}'
        value = read(entry + 2, length * (2 if header & 1 else 1)).decode('utf-16le' if header & 1 else 'utf-8')
        return value + (f'_{number-1}' if number else '')
    def dump(address, size):
        data = read(address, size)
        for offset in range(0, len(data) - 7, 8):
            value = struct.unpack_from('<Q', data, offset)[0]
            if value:
                print(f'+{offset:04x} {value:016x} floats={struct.unpack_from("<ff", data, offset)}')
    if a.address:
        dump(a.address, a.size)
    else:
        world = ptr(base + 0xb4ef3d8)
        print('WORLD', hex(world))
        instance = ptr(world + 0x228)
        print('INSTANCE', hex(instance))
        players = ptr(instance + 0x38)
        player = ptr(players)
        controller = ptr(player + 0x30)
        pawn = ptr(controller + 0x2e8)
        print('PLAYER', hex(player), 'CONTROLLER', hex(controller), 'PAWN', hex(pawn))
        if a.damage:
            print('GET COMPONENT RVA', hex(ptr(ptr(pawn) + 0x720) - base))
            for offset in (0x280, 0x290):
                array, count, capacity = struct.unpack('<Qii', read(pawn + offset, 16))
                print('COMPONENT ARRAY', hex(offset), count, capacity)
                if not 0 < count <= capacity <= 1024: continue
                for index in range(count):
                    component = ptr(array + index * 8)
                    if ptr(component) == base + 0x9c11ca8:
                        print('DAMAGE COMPONENT', hex(component), 'owner', hex(ptr(component + 0xa0)))
                        parts, n, cap = struct.unpack('<Qii', read(component + 0x118, 16))
                        if not 0 < n <= cap <= 128: continue
                        for i in range(n):
                            part = ptr(parts + i * 8)
                            print('PART', i, hex(part), 'id', name(part + 0x58), 'outer', hex(ptr(part + 0x20)),
                                  'max', struct.unpack('<f', read(part + 0xb8, 4))[0],
                                  'state/life', struct.unpack('<if', read(part + 0x130, 8)))
                            states, state_count, state_cap = struct.unpack('<Qii', read(part + 0xc0, 16))
                            if 0 < state_count <= state_cap <= 16:
                                print('STATES', [(name(states + j*32), read(states+j*32+8,1)[0], struct.unpack('<f',read(states+j*32+12,4))[0]) for j in range(state_count)])
        elif a.simulation:
            simulation = ptr(pawn + 0x580)
            print('SIMULATION', hex(simulation))
            for name, offset in [('wheels', 0x15e8), ('engine', 0x28c0)]:
                array = ptr(simulation + offset)
                if name == 'wheels': array = ptr(array)
                obj = ptr(array)
                vtable = ptr(obj)
                print(name, 'array', hex(array), 'object', hex(obj), 'vtable RVA', hex(vtable-base))
                print('methods', [(hex(o), hex(ptr(vtable+o)-base)) for o in range(0, 0x68, 8)])
                dump(obj, 0x160)
        elif a.controls:
            last = None
            for _ in range(6000):
                data = read(pawn + 0x610, 8)
                if data != last:
                    print(time.strftime('%H:%M:%S'), 'packed controls', data.hex(), 'handbrake', data[4] / 255, flush=True)
                    last = data
                time.sleep(0.1)
        else:
            dump(pawn, 0x700)
finally:
    k.CloseHandle(h)
