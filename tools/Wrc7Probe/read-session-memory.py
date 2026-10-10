"""Read-only, bounded WRC7 development probe for the inspected executable build.

Offsets are research findings, not a version-independent production API.
"""
import argparse
import ctypes as c
import struct
import hashlib
import time
from pathlib import Path
from ctypes import wintypes as w

parser = argparse.ArgumentParser()
parser.add_argument("pid", type=int)
parser.add_argument("base", type=lambda value: int(value, 0))
parser.add_argument("--watch-inputs", type=int, default=0, help="Observe input changes for up to 300 seconds")
args = parser.parse_args()
k = c.WinDLL("kernel32", use_last_error=True)
k.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
k.OpenProcess.restype = w.HANDLE
k.ReadProcessMemory.argtypes = [w.HANDLE, c.c_void_p, c.c_void_p, c.c_size_t, c.POINTER(c.c_size_t)]
k.QueryFullProcessImageNameW.argtypes = [w.HANDLE, w.DWORD, w.LPWSTR, c.POINTER(w.DWORD)]
k.CloseHandle.argtypes = [w.HANDLE]
handle = k.OpenProcess(0x1010, False, args.pid)
if not handle:
    raise OSError(c.get_last_error())

try:
    path = c.create_unicode_buffer(32768)
    length = w.DWORD(len(path))
    if not k.QueryFullProcessImageNameW(handle, 0, path, c.byref(length)) or not path.value.lower().endswith("\\wrc7.exe"):
        raise ValueError("Expected WRC7.exe")
    with Path(path.value).open("rb") as executable:
        digest = hashlib.file_digest(executable, "sha256").hexdigest()
    if digest != "0fee0ed14ad05d76fd44b2ba1215bacafc806dd9d7e8b313634a99c105903633":
        raise ValueError("Unverified WRC7 executable; fixed research offsets must not be used")

    def read(address, size):
        if not 65536 <= address <= 0x7fffffffffff or not 0 < size <= 4096:
            raise ValueError("Invalid read bounds")
        buffer = c.create_string_buffer(size)
        count = c.c_size_t()
        if not k.ReadProcessMemory(handle, address, buffer, size, c.byref(count)) or count.value != size:
            raise OSError(c.get_last_error())
        return buffer.raw

    def pointer(address):
        return struct.unpack("<Q", read(address, 8))[0]

    factory = pointer(args.base + 0x1091d78)
    print("ObjectFactory selective load fields:", read(factory + 0x108, 16).hex())
    engine = pointer(args.base + 0x1091d20)
    level = pointer(pointer(engine + 0x68) + 0x40)
    text = level + 0x38
    text_length, capacity = struct.unpack("<QQ", read(text + 16, 16))
    if not 0 < text_length < 512 or capacity < text_length:
        raise ValueError("Unrecognized level name layout")
    print("Active level:", read(pointer(text) if capacity >= 16 else text, text_length).decode("ascii"))

    manager = pointer(args.base + 0x1091b80)
    if pointer(manager) != args.base + 0xdb1b60:
        raise ValueError("Loaded WRC7 manager layout differs from the inspected build")
    profiles = pointer(manager + 0x17798)
    profiles_end = pointer(manager + 0x177a0)
    if not 0 < profiles_end - profiles <= 16 * 0x170 or (profiles_end - profiles) % 0x170:
        raise ValueError("Unrecognized profile layout")
    for profile in range(profiles, profiles_end, 0x170):
        car = pointer(profile + 0x20)
        name = read(pointer(car), 128).split(b"\0", 1)[0].decode("ascii")
        print("Profile car:", name)
        print("Car localized name ID:", struct.unpack("<I", read(car + 8, 4))[0])

    begin, end = pointer(manager + 0xb0), pointer(manager + 0xb8)
    if not 0 < end - begin <= 16 * 8 or (end - begin) % 8:
        raise ValueError("Unrecognized race participant layout")
    player_drive = None
    for index, entry in enumerate(range(begin, end, 8)):
        vehicle = pointer(entry)
        stats = pointer(vehicle + 0x38)
        raw = struct.unpack("<i", read(stats + 0x6c, 4))[0]
        penalty = struct.unpack("<i", read(stats + 0x92c, 4))[0]
        print(f"Participant {index}: raw_ms={raw}, penalty_ms={penalty} (candidate fields)")
        physics = pointer(vehicle + 0x80)
        if pointer(physics) != args.base + 0xdb8ea0 or pointer(physics + 0x68) != vehicle:
            raise ValueError("Unrecognized physics object")
        drive = pointer(physics + 0x90)
        if index == 0:
            player_drive = drive
        print("Drive inputs [0..5]:", struct.unpack("<6f", read(drive + 0xd0, 24)))
        print("Physics gear count candidate:", struct.unpack("<i", read(physics + 0x90c, 4))[0])
        print("Vehicle/stats:", hex(vehicle), hex(stats))
        print("Stats vtable RVA:", hex(pointer(stats) - args.base))
        print("Stats first 128 bytes:", read(stats, 128).hex())
        print("Vehicle race-state wrapper:", read(vehicle + 0x700, 24).hex())
        race = pointer(args.base + 0x10add50)
        track = pointer(race + 0x260)
        print("Track length / progress candidates:", struct.unpack("<f", read(track + 0x290, 4))[0], struct.unpack("<f", read(stats + 0x790, 4))[0])
        print("Finish flags:", read(stats + 0x8c8, 3).hex())
        body = pointer(drive + 0xb0)
        print("Rigid body vtable/getters:", hex(pointer(body)), hex(pointer(pointer(body) + 0xa0)), hex(pointer(pointer(body) + 0xf0)))
        print("Wheel radius values:", [struct.unpack("<f", read(pointer(drive + 0x30) + 0x1c0 + i * 0x30, 4))[0] for i in range(4)])
        print("Wheel angular speeds:", struct.unpack("<4f", read(pointer(drive + 0x60) + 0x10, 16)))
    deadline = time.monotonic() + min(300, max(0, args.watch_inputs))
    previous = None
    while player_drive and time.monotonic() < deadline:
        inputs = tuple(round(v, 2) for v in struct.unpack("<6f", read(player_drive + 0xd0, 24)))
        if inputs != previous:
            print(time.strftime("%H:%M:%S"), "inputs", inputs, flush=True)
            previous = inputs
        time.sleep(0.1)
finally:
    k.CloseHandle(handle)
