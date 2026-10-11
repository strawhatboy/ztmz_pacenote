"""Read-only ACR executable inspection for telemetry field provenance."""
import argparse
import bisect
import struct
import sys
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument("executable")
p.add_argument("--deps", required=True)
p.add_argument("--text", action='append')
p.add_argument("--rva", type=lambda s: int(s, 0), action='append')
p.add_argument("--size", type=lambda s: int(s, 0), default=256)
p.add_argument("--reference", type=lambda s: int(s, 0))
p.add_argument("--table", type=lambda s: int(s, 0))
p.add_argument("--strings-rva", type=lambda s: int(s, 0))
p.add_argument("--call", type=lambda s: int(s, 0))
p.add_argument('--filter')
p.add_argument('--displacement', type=lambda s: int(s, 0))
p.add_argument('--properties', type=lambda s: int(s, 0))
a = p.parse_args()
sys.path.insert(0, a.deps)
import pefile
import capstone

pe = pefile.PE(a.executable, fast_load=True)
pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_EXCEPTION']])
base = pe.OPTIONAL_HEADER.ImageBase
functions = sorted((e.struct.BeginAddress, e.struct.EndAddress) for e in pe.DIRECTORY_ENTRY_EXCEPTION)
begins = [s for s, _ in functions]
if a.properties is not None:
    for offset in range(0, a.size, 8):
        rva = a.properties + offset
        record = pe.get_data(rva, 64)
        pointer = struct.unpack_from('<Q', record)[0]
        if not base <= pointer < base + pe.OPTIONAL_HEADER.SizeOfImage: continue
        name = pe.get_data(pointer-base, 100).split(b'\0')[0]
        if not name or not all(32 <= b < 127 for b in name): continue
        kind = struct.unpack_from('<I', record, 24)[0]
        dim, field = struct.unpack_from('<HH', record, 48)
        if kind > 0x40 or dim != 1: continue
        print(hex(rva), name.decode('ascii'), 'kind', hex(kind), 'offset', hex(field), 'extra', hex(struct.unpack_from('<Q', record, 56)[0]-base))
if a.displacement is not None:
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    md.detail = True
    candidates = set()
    needle = struct.pack('<i', a.displacement)
    for section in pe.sections:
        if not section.Characteristics & 0x20000000: continue
        data = section.get_data()
        start = 0
        while (at := data.find(needle, start)) >= 0:
            rva = section.VirtualAddress + at
            index = bisect.bisect_right(begins, rva) - 1
            if index >= 0 and rva < functions[index][1]: candidates.add(index)
            start = at + 4
    for index in sorted(candidates):
        begin, end = functions[index]
        for ins in md.disasm(pe.get_data(begin, end-begin), base+begin):
            if any(op.type == capstone.CS_OP_MEM and op.mem.disp == a.displacement for op in ins.operands):
                if a.filter and a.filter not in ins.op_str: continue
                print(hex(ins.address-base), hex(begin), ins.mnemonic, ins.op_str)
targets = set([a.reference] if a.reference is not None else [])
if a.call is not None:
    import re
    for section in pe.sections:
        if not section.Characteristics & 0x20000000: continue
        data = section.get_data()
        for m in re.finditer(rb'[\xe8\xe9]', data):
            at = m.start()
            if at + 5 > len(data): continue
            rva = section.VirtualAddress + at
            if rva + 5 + struct.unpack_from('<i', data, at + 1)[0] == a.call:
                i = bisect.bisect_right(begins, rva) - 1
                print('CALL', hex(rva), 'FUNCTION', tuple(hex(n) for n in functions[i]))
if a.strings_rva is not None:
    import re
    for m in re.finditer(rb'[\x20-\x7e]{3,}', pe.get_data(a.strings_rva, a.size)):
        print(hex(a.strings_rva+m.start()), m.group().decode('ascii'))
if a.table is not None:
    for offset in range(0, a.size, 8):
        value = struct.unpack('<Q', pe.get_data(a.table + offset, 8))[0]
        label = ''
        if base <= value < base + pe.OPTIONAL_HEADER.SizeOfImage:
            raw = pe.get_data(value-base, 80).split(b'\0')[0]
            if raw and all(32 <= b < 127 for b in raw): label = raw.decode('ascii')
        print(hex(offset), hex(value-base), label)
if a.text:
    for section in pe.sections:
        data = section.get_data()
        for value in a.text:
            for encoding in ['utf-8', 'utf-16le']:
                needle = value.encode(encoding)
                start = 0
                while (at := data.find(needle, start)) >= 0:
                    target = section.VirtualAddress + at
                    targets.add(target)
                    print('STRING', hex(target), encoding, value)
                    start = at + len(needle)
if targets:
    for section in pe.sections:
        data = section.get_data()
        for target in targets:
            needle = struct.pack('<Q', base + target)
            start = 0
            while (at := data.find(needle, start)) >= 0:
                print('POINTER', hex(section.VirtualAddress + at), 'TARGET', hex(target))
                start = at + 8
    # RIP-relative references; filter instruction candidates before decoding.
    import re
    for section in pe.sections:
        if not section.Characteristics & 0x20000000:
            continue
        data = section.get_data()
        for m in re.finditer(rb'[\x48-\x4f]\x8d[\x05\x0d\x15\x1d\x25\x2d\x35\x3d]', data):
            at = m.start()
            rva = section.VirtualAddress + at
            target = rva + 7 + struct.unpack_from('<i', data, at + 3)[0]
            if target in targets:
                i = bisect.bisect_right(begins, rva) - 1
                print('XREF', hex(rva), 'FUNCTION', tuple(hex(n) for n in functions[i]), 'TARGET', hex(target))
if a.rva is not None:
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    md.skipdata = True
    for rva in a.rva:
        for ins in md.disasm(pe.get_data(rva, a.size), base + rva):
            if a.filter and a.filter not in ins.op_str: continue
            print(f'{ins.address-base:08x} {ins.mnemonic:9} {ins.op_str}')
