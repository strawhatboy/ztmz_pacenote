"""Read-only development inspector; no game writes or process injection."""
import argparse
import bisect
import re
import struct
import sys
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("executable", type=Path)
parser.add_argument("--deps", type=Path)
parser.add_argument("--find")
parser.add_argument("--rva", type=lambda x: int(x, 0))
parser.add_argument("--size", type=lambda x: int(x, 0), default=1024)
parser.add_argument("--references", type=lambda x: int(x, 0))
parser.add_argument("--imports", action="store_true")
args = parser.parse_args()
if args.deps:
    sys.path.insert(0, str(args.deps))
import pefile
import capstone

pe = pefile.PE(str(args.executable))
base = pe.OPTIONAL_HEADER.ImageBase
functions = sorted((e.struct.BeginAddress, e.struct.EndAddress) for e in pe.DIRECTORY_ENTRY_EXCEPTION)
begins = [a for a, b in functions]
def enclosing(rva):
    index = bisect.bisect_right(begins, rva) - 1
    return functions[index] if index >= 0 and rva < functions[index][1] else (rva, rva + 256)

if args.imports:
    for module in pe.DIRECTORY_ENTRY_IMPORT:
        for entry in module.imports:
            if entry.name and re.search("send|socket|writefile", entry.name.decode(), re.I):
                print(hex(entry.address-base), entry.name.decode())
if args.references is not None:
    for section in pe.sections:
        if not section.Characteristics & 0x20000000:
            continue
        data = section.get_data()
        for offset in range(len(data)-4):
            if section.VirtualAddress+offset+4+struct.unpack_from("<i", data, offset)[0] == args.references:
                rva = section.VirtualAddress+offset
                print("REFERENCE",hex(rva),"FUNCTION",*[hex(v) for v in enclosing(rva)],"BYTES",data[max(0,offset-4):offset+4].hex())

if args.find:
    targets = {}
    pattern = re.compile(args.find, re.I)
    for section in pe.sections:
        for match in re.finditer(rb"[\x20-\x7e]{8,}", section.get_data()):
            value = match[0].decode("ascii")
            if pattern.search(value):
                rva = section.VirtualAddress + match.start()
                targets[rva] = value
                print(f"STRING {rva:#x} {value[:350]}")
    for section in pe.sections:
        if not section.Characteristics & 0x20000000:
            continue
        data = section.get_data()
        for match in re.finditer(rb"[\x48-\x4f]\x8d[\x05\x0d\x15\x1d\x25\x2d\x35\x3d]", data):
            offset = match.start()
            target = section.VirtualAddress + offset + 7 + struct.unpack_from("<i", data, offset + 3)[0]
            if target in targets:
                rva = section.VirtualAddress + offset
                start, end = enclosing(rva)
                print(f"XREF {rva:#x} FUNCTION {start:#x}..{end:#x} -> {targets[target][:180]}")
if args.rva is not None:
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    for instruction in md.disasm(pe.get_data(args.rva, args.size), base + args.rva):
        print(f"{instruction.address-base:08x} {instruction.mnemonic:9} {instruction.op_str}")
