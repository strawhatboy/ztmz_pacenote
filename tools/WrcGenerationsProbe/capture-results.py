"""Read-only, build-gated WRCG result investigation. Emits raw evidence, not events."""
import argparse
import ctypes as c
from ctypes import wintypes as w
import hashlib
import json
from pathlib import Path
import struct
import sys
import time


def sample(read, base):
    def ptr(address):
        return struct.unpack('<Q', read(address, 8))[0]

    def integer(address):
        return struct.unpack('<i', read(address, 4))[0]

    manager = ptr(base + 0x1c61660)
    if not manager:
        return {'available': False}
    race = ptr(manager + 0x48)
    profiles = ptr(manager + 0x1378)
    count = integer(manager + 0x1374)
    player = read(manager + 0x1380, 1)[0]
    stage = ptr(manager + 0xd50)
    mode, submode = integer(manager + 0x12d0), integer(manager + 0x12e0)
    if not profiles or not 1 <= count <= 64 or player >= count:
        return {'available': False}
    profile = profiles + player * 0x210
    result = read(profile + 0x19c, 10)
    timer = 0
    flags = None
    participant = 0
    timer_index = player if mode == 7 and submode != 5 else 0
    if race and ptr(base + 0x1d61938) == race and timer_index < 50:
        timer = ptr(race + 0x4e8 + timer_index * 8)
        if timer:
            flags = read(timer + 0x9c, 4)
            participant = ptr(timer + 0xa0)
            if not participant or ptr(participant + 0x38) != timer:
                raise OSError('Timer ownership changed')
    if (ptr(base + 0x1c61660) != manager or ptr(manager + 0x48) != race or
            ptr(manager + 0x1378) != profiles or integer(manager + 0x1374) != count or
            read(manager + 0x1380, 1)[0] != player or ptr(manager + 0xd50) != stage or
            integer(manager + 0x12d0) != mode or integer(manager + 0x12e0) != submode or
            read(profile + 0x19c, 10) != result):
        raise OSError('Scene/result changed during read')
    if timer and (ptr(base + 0x1d61938) != race or
                  ptr(race + 0x4e8 + timer_index * 8) != timer or
                  ptr(timer + 0xa0) != participant or ptr(participant + 0x38) != timer or
                  read(timer + 0x9c, 4) != flags):
        raise OSError('Timer changed during read')
    return {'available': True, 'manager': hex(manager), 'race': hex(race),
            'stage': hex(stage), 'profile': hex(profile), 'player': player,
            'mode': mode, 'submode': submode, 'timer': hex(timer),
            'timerFlags9cTo9f': list(flags) if flags is not None else None,
            'profileRawMilliseconds': struct.unpack_from('<i', result)[0],
            'profileTotalMilliseconds': struct.unpack_from('<i', result, 4)[0],
            'profileReady1a4': result[8], 'profileFlag1a5': result[9]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('pid', type=int)
    parser.add_argument('base', type=lambda text: int(text, 0))
    parser.add_argument('--seconds', type=int, default=300, choices=range(1, 1801), metavar='1..1800')
    parser.add_argument('--output', type=Path, help='Create a new JSONL file and flush each event immediately')
    args = parser.parse_args()
    k = c.WinDLL('kernel32', use_last_error=True)
    k.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
    k.OpenProcess.restype = w.HANDLE
    k.ReadProcessMemory.argtypes = [w.HANDLE, c.c_void_p, c.c_void_p, c.c_size_t, c.POINTER(c.c_size_t)]
    k.ReadProcessMemory.restype = w.BOOL
    k.QueryFullProcessImageNameW.argtypes = [w.HANDLE, w.DWORD, w.LPWSTR, c.POINTER(w.DWORD)]
    k.QueryFullProcessImageNameW.restype = w.BOOL
    k.CloseHandle.argtypes = [w.HANDLE]
    k.CloseHandle.restype = w.BOOL
    handle = k.OpenProcess(0x1010, False, args.pid)
    if not handle:
        raise c.WinError(c.get_last_error())
    output = None
    try:
        output = args.output.open('x', encoding='utf-8') if args.output else sys.stdout
        path = c.create_unicode_buffer(32768)
        length = w.DWORD(len(path))
        if not k.QueryFullProcessImageNameW(handle, 0, path, c.byref(length)):
            raise c.WinError(c.get_last_error())
        executable = Path(path.value)
        if executable.name.lower() != 'wrcg.exe':
            raise ValueError('Not a WRCG process')
        with executable.open('rb') as source:
            digest = hashlib.file_digest(source, 'sha256').hexdigest().upper()
        if digest != 'C2D711BE210F4E1EA0E97882D070C359E66703CC6C40D39D9FC0E5FAF7824D2D':
            raise ValueError('Unverified WRCG executable build')

        def read(address, size):
            if not 0x10000 <= address <= 0x7fffffffffff - size or not 1 <= size <= 8192:
                raise OSError('Invalid memory range')
            buffer = c.create_string_buffer(size)
            received = c.c_size_t()
            if not k.ReadProcessMemory(handle, address, buffer, size, c.byref(received)) or received.value != size:
                raise OSError('Memory unavailable')
            return buffer.raw

        if read(args.base + 0x837130, 8) != bytes.fromhex('488b81500d0000c3'):
            raise ValueError('Loaded image/base does not match the verified getter')
        print(json.dumps({'event': 'ready', 'pid': args.pid, 'sha256': digest,
                          'base': hex(args.base), 'unixTime': time.time()}), file=output, flush=True)
        started = time.monotonic()
        previous = None
        while time.monotonic() - started < args.seconds:
            try:
                current = sample(read, args.base)
            except OSError:
                current = {'available': False}
            if current != previous:
                print(json.dumps({'elapsedMs': round((time.monotonic() - started) * 1000), **current}), file=output, flush=True)
                previous = current
            time.sleep(0.02)
        print(json.dumps({'event': 'capture-ended'}), file=output, flush=True)
    finally:
        k.CloseHandle(handle)
        if output is not None and output is not sys.stdout:
            output.close()


if __name__ == '__main__':
    main()
