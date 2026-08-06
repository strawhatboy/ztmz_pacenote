import ida_auto
import ida_bytes
import ida_funcs
import ida_idaapi
import ida_name
import ida_pro
import ida_segment
import idautils


OUTPUT_PATH = r"C:\Users\Andy\lab\ztmz_pacenote\tmp\acr_fmod_start_xrefs.txt"
NEEDLE = "?start@EventInstance@Studio@FMOD"


def write_line(handle, value=""):
    handle.write(value + "\n")


ida_auto.auto_wait()

matches = [(address, name) for address, name in idautils.Names() if NEEDLE in name]
with open(OUTPUT_PATH, "w", encoding="utf-8") as output:
    write_line(output, "FMOD EventInstance::start symbols")
    for address, name in matches:
        write_line(output, f"{address:#x} {name}")

    for address, name in matches:
        write_line(output)
        write_line(output, f"xrefs to {address:#x} {name}")
        for xref in idautils.XrefsTo(address):
            function = ida_funcs.get_func(xref.frm)
            function_start = function.start_ea if function else ida_idaapi.BADADDR
            function_name = ida_name.get_name(function_start) if function else "<no-function>"
            segment = ida_segment.getseg(xref.frm)
            segment_name = ida_segment.get_segm_name(segment) if segment else "<no-segment>"
            raw = ida_bytes.get_bytes(max(0, xref.frm - 32), 80)
            raw_hex = raw.hex(" ") if raw else "<unavailable>"
            write_line(
                output,
                f"from={xref.frm:#x} type={xref.type} function={function_start:#x} {function_name} segment={segment_name}",
            )
            write_line(output, f"bytes={raw_hex}")

ida_pro.qexit(0)
