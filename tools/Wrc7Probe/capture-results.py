"""Read-only WRC7 result capture; raw flags are evidence, not inferred events."""
import argparse
import ctypes as c
from ctypes import wintypes as w
import hashlib
import json
from pathlib import Path
import struct
import time


def sample(read, base):
    def ptr(address):
        return struct.unpack('<Q', read(address, 8))[0]
    manager = ptr(base + 0x1091b80)
    if not manager or ptr(manager) != base + 0xdb1b60:
        raise OSError('Manager unavailable')
    profile = ptr(manager + 0x17798)
    if not profile or ptr(manager + 0x177a0) - profile != 0x170:
        raise OSError('Only verified single-player profile selection supported')
    participants, end = ptr(manager + 0xb0), ptr(manager + 0xb8)
    if not participants or not 8 <= end - participants <= 128 or (end - participants) % 8:
        raise OSError('Participants unavailable')
    vehicle = ptr(participants)
    stats = ptr(vehicle + 0x38)
    if not stats or ptr(stats + 0x908) != vehicle:
        raise OSError('Race stats ownership mismatch')
    flags = read(stats + 0x8c8, 3)
    result = read(profile + 0x154, 14)
    raw = struct.unpack('<i', read(stats + 0x6c, 4))[0]
    penalty = struct.unpack('<i', read(stats + 0x92c, 4))[0]
    if (ptr(base + 0x1091b80) != manager or ptr(manager + 0x17798) != profile or
            ptr(manager + 0x177a0) - profile != 0x170 or ptr(manager + 0xb0) != participants or
            ptr(manager + 0xb8) != end or ptr(participants) != vehicle or
            ptr(vehicle + 0x38) != stats or ptr(stats + 0x908) != vehicle or
            read(stats + 0x8c8, 3) != flags or read(profile + 0x154, 14) != result):
        raise OSError('Scene/result changed during read')
    return {'available': True, 'manager': hex(manager), 'profile': hex(profile),
            'vehicle': hex(vehicle), 'stats': hex(stats), 'rawMs': raw, 'penaltyMs': penalty,
            'statsFlags8c8To8ca': list(flags), 'profileRawMs': struct.unpack_from('<i', result)[0],
            'profileTotalMs': struct.unpack_from('<i', result, 4)[0],
            'profileFlags160To161': list(result[12:14])}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('pid', type=int)
    parser.add_argument('base', type=lambda value: int(value, 0))
    parser.add_argument('--seconds', type=int, default=600, choices=range(0, 1801), metavar='0..1800',
                        help='0 captures until the game exits or Ctrl+C; otherwise a time limit in seconds')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    k = c.WinDLL('kernel32', use_last_error=True)
    k.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
    k.OpenProcess.restype = w.HANDLE
    k.ReadProcessMemory.argtypes = [w.HANDLE, c.c_void_p, c.c_void_p, c.c_size_t, c.POINTER(c.c_size_t)]
    k.ReadProcessMemory.restype = w.BOOL
    k.QueryFullProcessImageNameW.argtypes = [w.HANDLE, w.DWORD, w.LPWSTR, c.POINTER(w.DWORD)]
    k.QueryFullProcessImageNameW.restype = w.BOOL
    k.CloseHandle.argtypes = [w.HANDLE]
    k.GetExitCodeProcess.argtypes = [w.HANDLE, c.POINTER(w.DWORD)]
    k.GetExitCodeProcess.restype = w.BOOL
    handle = k.OpenProcess(0x1010, False, args.pid)
    if not handle:
        raise c.WinError(c.get_last_error())
    try:
        path = c.create_unicode_buffer(32768)
        length = w.DWORD(len(path))
        if not k.QueryFullProcessImageNameW(handle, 0, path, c.byref(length)):
            raise c.WinError(c.get_last_error())
        executable = Path(path.value)
        if executable.name.lower() != 'wrc7.exe':
            raise ValueError('Not WRC7.exe')
        with executable.open('rb') as stream:
            digest = hashlib.file_digest(stream, 'sha256').hexdigest().upper()
        if digest != '0FEE0ED14AD05D76FD44B2BA1215BACAFC806DD9D7E8B313634A99C105903633':
            raise ValueError('Unverified executable')
        def read(address, size):
            if not 0x10000 <= address <= 0x7fffffffffff - size or not 1 <= size <= 4096:
                raise OSError('Invalid memory bounds')
            buffer, count = c.create_string_buffer(size), c.c_size_t()
            if not k.ReadProcessMemory(handle, address, buffer, size, c.byref(count)) or count.value != size:
                raise OSError('Memory unavailable')
            return buffer.raw
        with args.output.open('x', encoding='utf-8') as output:
            print(json.dumps({'event': 'ready', 'pid': args.pid, 'base': hex(args.base),
                              'sha256': digest, 'unixTime': time.time()}), file=output, flush=True)
            started, last_write, previous = time.monotonic(), 0, None
            while args.seconds == 0 or time.monotonic() - started < args.seconds:
                exit_code = w.DWORD()
                if not k.GetExitCodeProcess(handle, c.byref(exit_code)) or exit_code.value != 259:
                    break
                try:
                    current = sample(read, args.base)
                except OSError:
                    current = {'available': False}
                key = {key: value for key, value in current.items() if key not in ('rawMs', 'penaltyMs')}
                now = time.monotonic()
                if key != previous or now - last_write >= 1:
                    print(json.dumps({'elapsedMs': round((now - started) * 1000), **current}), file=output, flush=True)
                    previous, last_write = key, now
                time.sleep(0.02)
            print(json.dumps({'event': 'capture-ended'}), file=output, flush=True)
    finally:
        k.CloseHandle(handle)


if __name__ == '__main__':
    main()
