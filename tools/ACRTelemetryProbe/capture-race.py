"""Capture ACR's native race phase read-only; does not infer a ZTMZ result."""
import argparse
import ctypes as c
from ctypes import wintypes as w
import hashlib
import json
import math
from pathlib import Path
import struct
import time

SUPPORTED_HASH = '42C359B0CD24774A86441710FDC7D7DB3BC06B57B3FF30CB9EA5D56FB71448FB'
PHASES = ('None', 'Inited', 'Pre', 'StartSequence', 'PreSemaphore', 'Semaphore',
          'Running', 'Ended', 'EndSequence', 'Post', 'Resetting', 'Retire', 'Disqualify')


def sample(read, base):
    # These pointers and discrete fields must survive a second read. Clocks may
    # advance independently, so they are recorded but not used as consistency locks.
    guards = {}

    def stable(address, size):
        value = read(address, size)
        guards[(address, size)] = value
        return value

    def ptr(address):
        value = struct.unpack('<Q', stable(address, 8))[0]
        if value < 0x10000 or value > 0x7fffffffffff or value & 7:
            raise OSError('Pointer unavailable')
        return value

    world = ptr(base + 0xb4ef3d8)
    instance = ptr(world + 0x228)
    players = ptr(instance + 0x38)
    if struct.unpack('<i', stable(instance + 0x40, 4))[0] != 1:
        raise OSError('Only verified local-player selection supported')
    player = ptr(players)
    controller = ptr(player + 0x30)
    player_state = ptr(controller + 0x2b0)
    managers = set()
    for offset in (0x280, 0x290):
        array, count, capacity = struct.unpack('<Qii', stable(controller + offset, 16))
        if not 0 <= count <= capacity <= 1024:
            raise OSError('Invalid component array')
        for i in range(count):
            component = ptr(array + i * 8)
            if ptr(component) == base + 0x9dd8c98 and ptr(component + 0xa0) == controller:
                managers.add(component)
    if len(managers) != 1:
        raise OSError('Local RallyManager unavailable or ambiguous')
    manager = managers.pop()
    state = ptr(manager + 0x200)
    if ptr(state) != base + 0x9dd5da0 or ptr(state + 0xa0) != player_state:
        raise OSError('RaceStateData type/ownership mismatch')
    race_id = struct.unpack('<i', stable(state + 0x110, 4))[0]
    phase = struct.unpack('<i', stable(state + 0x120, 4))[0]
    race_time, start_time = struct.unpack('<ff', read(state + 0x118, 8))
    if phase not in range(len(PHASES)) or not all(map(math.isfinite, (race_time, start_time))):
        raise OSError('Invalid race state')
    for (address, size), value in guards.items():
        if read(address, size) != value:
            raise OSError('Scene/phase changed during read')
    return dict(available=True, world=hex(world), controller=hex(controller),
                playerState=hex(player_state), manager=hex(manager), state=hex(state),
                raceId=race_id, phase=phase, phaseName=PHASES[phase],
                raceTime=race_time, startTime=start_time)


def sample_results(read, base):
    guards = {}

    def stable(address, size):
        value = read(address, size)
        guards[(address, size)] = value
        return value

    def ptr(address):
        value = struct.unpack('<Q', stable(address, 8))[0]
        if value < 0x10000 or value > 0x7fffffffffff or value & 7:
            raise OSError('Result pointer unavailable')
        return value

    def component(actor, vtable):
        array, count, capacity = struct.unpack('<Qii', stable(actor + 0x230, 16))
        bit_count = struct.unpack('<i', stable(actor + 0x258, 4))[0]
        if not 0 <= bit_count == count <= capacity <= 1024:
            raise OSError('Invalid owned component set')
        flags = struct.unpack('<Q', stable(actor + 0x250, 8))[0] or actor + 0x240
        matches = set()
        for i in range(count):
            word = struct.unpack('<I', stable(flags + (i // 32) * 4, 4))[0]
            if word & (1 << (i % 32)):
                candidate = ptr(array + i * 16)
                if ptr(candidate) == base + vtable and ptr(candidate + 0xa0) == actor:
                    matches.add(candidate)
        if len(matches) != 1:
            raise OSError('Result component unavailable or ambiguous')
        return matches.pop()

    world = ptr(base + 0xb4ef3d8)
    instance = ptr(world + 0x228)
    players = ptr(instance + 0x38)
    if struct.unpack('<i', stable(instance + 0x40, 4))[0] != 1:
        raise OSError('Only verified local-player selection supported')
    player = ptr(players)
    controller = ptr(player + 0x30)
    player_state = ptr(controller + 0x2b0)
    participant = stable(player_state + 0x368, 8)
    game_state = ptr(world + 0x1b0)
    manager = component(game_state, 0x9ddd0e0)
    event = ptr(manager + 0xd0)
    result_component = component(event, 0x9dd1680)
    entries, count, capacity = struct.unpack('<Qii', stable(result_component + 0xe8, 16))
    if not 0 <= count <= capacity <= 1024:
        raise OSError('Invalid participant results array')
    matches = []
    for i in range(count):
        address = entries + i * 0x18
        if stable(address, 8) == participant:
            matches.append(address)
    if len(matches) > 1:
        raise OSError('Ambiguous participant results')
    results = []
    if matches:
        array, count, capacity = struct.unpack('<Qii', stable(matches[0] + 8, 16))
        if not 0 <= count <= capacity <= 1024:
            raise OSError('Invalid local results array')
        for i in range(count):
            address = array + i * 0x30
            race_id = struct.unpack('<i', stable(address, 4))[0]
            dnf, dq = stable(address + 0x28, 2)
            if dnf > 1 or dq > 1:
                raise OSError('Invalid result flags')
            results.append(dict(raceId=race_id, dnf=dnf, dq=dq))
    for (address, size), value in guards.items():
        if read(address, size) != value:
            raise OSError('Result changed during read')
    return dict(available=True, world=hex(world), controller=hex(controller),
                event=hex(event), component=hex(result_component), results=results)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('pid', type=int)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    k = c.WinDLL('kernel32', use_last_error=True)
    k.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
    k.OpenProcess.restype = w.HANDLE
    k.ReadProcessMemory.argtypes = [w.HANDLE, c.c_void_p, c.c_void_p, c.c_size_t, c.POINTER(c.c_size_t)]
    k.ReadProcessMemory.restype = w.BOOL
    k.QueryFullProcessImageNameW.argtypes = [w.HANDLE, w.DWORD, w.LPWSTR, c.POINTER(w.DWORD)]
    k.QueryFullProcessImageNameW.restype = w.BOOL
    k.GetExitCodeProcess.argtypes = [w.HANDLE, c.POINTER(w.DWORD)]
    k.GetExitCodeProcess.restype = w.BOOL
    k.CloseHandle.argtypes = [w.HANDLE]
    k.GetTickCount64.restype = c.c_ulonglong
    ps = c.WinDLL('psapi')
    ps.EnumProcessModulesEx.argtypes = [w.HANDLE, c.POINTER(c.c_void_p), w.DWORD, c.POINTER(w.DWORD), w.DWORD]
    ps.EnumProcessModulesEx.restype = w.BOOL
    handle = k.OpenProcess(0x410, False, args.pid)
    if not handle:
        raise c.WinError(c.get_last_error())
    try:
        path = c.create_unicode_buffer(32768)
        length = w.DWORD(len(path))
        if not k.QueryFullProcessImageNameW(handle, 0, path, c.byref(length)):
            raise c.WinError(c.get_last_error())
        executable = Path(path.value)
        if executable.name.lower() != 'acr.exe':
            raise ValueError('Not ACR.exe')
        with executable.open('rb') as stream:
            digest = hashlib.file_digest(stream, 'sha256').hexdigest().upper()
        if digest != SUPPORTED_HASH:
            raise ValueError('Unverified executable')
        modules, needed = (c.c_void_p * 1024)(), w.DWORD()
        if not ps.EnumProcessModulesEx(handle, modules, c.sizeof(modules), c.byref(needed), 3) or needed.value < 8:
            raise c.WinError(c.get_last_error())
        base = modules[0]

        def read(address, size):
            if not 0x10000 <= address <= 0x7fffffffffff - size or not 1 <= size <= 4096:
                raise OSError('Invalid memory bounds')
            buffer, count = c.create_string_buffer(size), c.c_size_t()
            if not k.ReadProcessMemory(handle, address, buffer, size, c.byref(count)) or count.value != size:
                raise OSError('Memory unavailable')
            return buffer.raw

        with args.output.open('x', encoding='utf-8') as output:
            def emit(record):
                line = json.dumps(dict(tickMs=k.GetTickCount64(), **record))
                print(line, file=output, flush=True)
                return line

            print(emit(dict(event='ready', pid=args.pid, sha256=digest, base=hex(base))), flush=True)
            previous, last_write = None, 0
            try:
                while True:
                    exit_code = w.DWORD()
                    if not k.GetExitCodeProcess(handle, c.byref(exit_code)) or exit_code.value != 259:
                        break
                    try:
                        current = sample(read, base)
                    except OSError as error:
                        current = dict(available=False, reason=str(error))
                    try:
                        current['resultData'] = sample_results(read, base)
                    except OSError as error:
                        current['resultData'] = dict(available=False, reason=str(error))
                    key = {k: v for k, v in current.items() if k not in ('raceTime', 'startTime')}
                    now = time.monotonic()
                    if key != previous or now - last_write >= 1:
                        line = emit(current)
                        if key != previous:
                            print(line, flush=True)
                        previous, last_write = key, now
                    time.sleep(0.02)
            except KeyboardInterrupt:
                pass
            print(emit(dict(event='capture-ended')), flush=True)
    finally:
        k.CloseHandle(handle)


if __name__ == '__main__':
    main()
