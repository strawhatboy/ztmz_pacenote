"""Read-only static-analysis probe for the third-party RBRHUD plugin.

Run from IDA's x86 batch executable. Set RBRHUD_IDA_REPORT to choose the
report file. The script neither attaches to a process nor modifies the input
binary; IDA's database location is controlled by the invoking command.
"""

import os
import traceback

import ida_auto
import ida_funcs
import ida_ida
import ida_idaapi
import ida_lines
import ida_name
import ida_nalt
import ida_pro
import idautils
import idc

try:
    import ida_hexrays
except ImportError:
    ida_hexrays = None


REPORT_PATH = os.environ.get(
    "RBRHUD_IDA_REPORT", os.path.join(os.getcwd(), "RBRHUD_ida_report.txt")
)

MEMORY_APIS = {
    "ReadProcessMemory",
    "WriteProcessMemory",
    "VirtualQueryEx",
    "VirtualProtect",
}

INTERESTING_STRINGS = (
    "damage_telemetry",
    "damageCalcMode",
    "Failed to acquire damage telemetry object",
    "Failed to load damage telemetry",
    "Car data already loaded",
    "Car data cannot be updated",
    "CTelemetryDamage",
    "EngineRPMLimit",
    "OilPressure",
    "RadiatorCoolantTemperature",
)

# A focused follow-up can add telemetry names without changing the standard
# report. For example: EngineOff,CoolantTemperatureWarning.
EXTRA_INTERESTING_STRINGS = tuple(
    value.strip()
    for value in os.environ.get("RBRHUD_IDA_EXTRA_STRINGS", "").split(",")
    if value.strip()
)

TARGET_FUNCTIONS = (
    ("Process-memory read helper", 0x10510710),
    ("Process-memory helper caller A", 0x10423410),
    ("Process-memory helper caller B", 0x10427300),
    ("Process-memory helper caller C", 0x10511250),
    ("Process-memory helper caller D", 0x10511FB0),
    ("Process-memory helper caller E", 0x10517740),
    ("Car-data loader/update", 0x102A4940),
    ("Damage telemetry loader", 0x10311460),
    ("Damage telemetry object acquisition", 0x103120C0),
    ("Damage telemetry factory lookup", 0x10375A70),
    ("Damage telemetry type registry", 0x103757B0),
    ("Damage telemetry registry population", 0x10375700),
    ("Shared damage telemetry metadata method", 0x103782F0),
    ("Shared damage telemetry update method", 0x10378370),
    ("Shared damage telemetry value method", 0x10378750),
    ("Battery override 1", 0x1037CE70),
    ("Battery override 3", 0x1037CEA0),
    ("Battery override 4", 0x1037CEB0),
    ("Battery override 6", 0x1037CED0),
    ("Battery override 7", 0x1037CFF0),
    ("Battery override 8", 0x1037D010),
    ("Battery override 9", 0x1037D020),
    ("Battery override 10", 0x1037D030),
    ("Battery override 11", 0x1037D040),
    ("Battery override 14", 0x1037D050),
    ("RBRHUD game-data singleton accessor", 0x1041CBF0),
    ("Damage conversion helper 0x1041D0F0", 0x1041D0F0),
    ("Damage conversion helper 0x1041D140", 0x1041D140),
    ("Damage conversion helper 0x1041D170", 0x1041D170),
    ("Damage conversion helper 0x1041D190", 0x1041D190),
    ("Damage conversion helper 0x1041D1D0", 0x1041D1D0),
    ("Damage conversion helper 0x1041D230", 0x1041D230),
    ("RBRHUD game-data read helper", 0x1041D290),
    ("Damage conversion helper 0x1041D320", 0x1041D320),
    ("Damage conversion helper 0x1041D370", 0x1041D370),
    ("Damage conversion helper 0x1041D3D0", 0x1041D3D0),
    ("Damage conversion helper 0x1041D430", 0x1041D430),
    ("Damage conversion helper 0x1041D490", 0x1041D490),
    ("Damage conversion helper 0x1041D740", 0x1041D740),
    ("Damage conversion helper 0x1041D870", 0x1041D870),
    ("Damage conversion helper 0x1041D950", 0x1041D950),
    ("Damage conversion helper 0x1041DAE0", 0x1041DAE0),
    ("Damage conversion helper 0x1041DC70", 0x1041DC70),
    ("Damage conversion helper 0x1041DCF0", 0x1041DCF0),
    ("Damage conversion helper 0x1041DD60", 0x1041DD60),
    ("Damage telemetry construction entry point", 0x10318500),
)

DAMAGE_VTABLES = (
    ("Battery", "??_7CTelemetryDamageBattery@@6B@"),
    ("Brake caliper LF", "??_7CTelemetryDamageBrakeCaliperLF@@6B@"),
    ("Gearbox", "??_7CTelemetryDamageGearbox@@6B@"),
    ("Radiator", "??_7CTelemetryDamageRadiator@@6B@"),
    ("Tyre LF", "??_7CTelemetryDamageTyreLF@@6B@"),
    ("Wheel LF", "??_7CTelemetryDamageWheelLF@@6B@"),
    ("Water pump", "??_7CTelemetryDamageWaterPump@@6B@"),
)

WARNING_TELEMETRY_VTABLES = (
    ("Engine off", "??_7CTelemetryEngineOff@@6B@"),
    ("Engine on", "??_7CTelemetryEngineOn@@6B@"),
    ("Coolant temperature warning", "??_7CTelemetryCoolantTemperatureWarning@@6B@"),
    ("Coolant temperature", "??_7CTelemetryCoolantTemperatureC@@6B@"),
    ("Radiator coolant temperature", "??_7CTelemetryRadiatorCoolantTemperatureC@@6B@"),
)


def ea_text(ea):
    return "0x{:08X}".format(ea)


def symbol_at(ea):
    return ida_name.get_name(ea) or idc.get_name(ea) or "<unnamed>"


def containing_function(ea):
    function = ida_funcs.get_func(ea)
    if function is None:
        return None
    return function


def function_text(function):
    if function is None:
        return "<no containing function>"
    return "{} ({})".format(symbol_at(function.start_ea), ea_text(function.start_ea))


def instruction_window(ea, before=6, after=12):
    function = containing_function(ea)
    if function is None:
        return []

    instructions = list(idautils.FuncItems(function.start_ea))
    try:
        index = instructions.index(ea)
    except ValueError:
        index = next(
            (i for i, item_ea in enumerate(instructions) if item_ea >= ea),
            len(instructions),
        )

    lines = []
    first = max(0, index - before)
    last = min(len(instructions), index + after + 1)
    for item_ea in instructions[first:last]:
        disassembly = idc.generate_disasm_line(item_ea, 0) or ""
        marker = " =>" if item_ea == ea else "   "
        lines.append("{} {} {}".format(marker, ea_text(item_ea), disassembly))
    return lines


def data_window(ea, length=64):
    lines = []
    for item_ea in range(ea, ea + length, 4):
        value = idc.get_wide_dword(item_ea)
        lines.append(
            "  {}: {:08X} {}".format(ea_text(item_ea), value, symbol_at(value))
        )
    return lines


def find_imports():
    imports = []
    for module_index in range(ida_nalt.get_import_module_qty()):
        module_name = ida_nalt.get_import_module_name(module_index) or "<unnamed>"

        def callback(ea, name, ordinal):
            if name in MEMORY_APIS:
                imports.append((module_name, ea, name, ordinal))
            return True

        ida_nalt.enum_import_names(module_index, callback)
    return imports


def find_interesting_strings():
    matches = []
    strings = idautils.Strings()
    for string in strings:
        value = str(string)
        if any(
            term.lower() in value.lower()
            for term in INTERESTING_STRINGS + EXTRA_INTERESTING_STRINGS
        ):
            matches.append((string.ea, value))
    return matches


def direct_callees(function):
    callees = []
    seen = set()
    for item_ea in idautils.FuncItems(function.start_ea):
        mnemonic = idc.print_insn_mnem(item_ea).lower()
        if mnemonic not in ("call", "jmp"):
            continue
        target_ea = idc.get_operand_value(item_ea, 0)
        key = (item_ea, target_ea)
        if key in seen:
            continue
        seen.add(key)
        callee = containing_function(target_ea)
        callees.append((item_ea, target_ea, callee))
    return callees


def report_target_function(report, label, start_ea, hexrays_ready):
    function = containing_function(start_ea)
    emit(report, "{}: {}".format(label, function_text(function)))
    if function is None:
        emit(report, "  Function was not found in this database.")
        return

    callers = list(idautils.CodeRefsTo(function.start_ea, False))
    emit(report, "  Direct callers: {}".format(len(callers)))
    for caller_ea in callers:
        emit(
            report,
            "    {} in {}".format(
                ea_text(caller_ea), function_text(containing_function(caller_ea))
            ),
        )

    callees = direct_callees(function)
    emit(report, "  Direct calls/jumps: {}".format(len(callees)))
    for call_ea, target_ea, callee in callees:
        target_text = function_text(callee) if callee else symbol_at(target_ea)
        emit(
            report,
            "    {} -> {} ({})".format(ea_text(call_ea), ea_text(target_ea), target_text),
        )

    if hexrays_ready:
        try:
            cfunc = ida_hexrays.decompile(function.start_ea)
            emit(report, "  Hex-Rays pseudocode:")
            if cfunc is None:
                emit(report, "    <decompilation returned no function>")
            else:
                for line in cfunc.get_pseudocode():
                    emit(report, "    " + ida_lines.tag_remove(line.line))
        except Exception as error:
            emit(report, "  Hex-Rays failed: {}".format(error))
    else:
        emit(report, "  Hex-Rays pseudocode: unavailable")

    emit(report, "  Disassembly:")
    item_count = 0
    for item_ea in idautils.FuncItems(function.start_ea):
        emit(
            report,
            "    {} {}".format(
                ea_text(item_ea), idc.generate_disasm_line(item_ea, 0) or ""
            ),
        )
        item_count += 1
        if item_count >= 400:
            emit(report, "    <truncated after 400 instructions>")
            break


def report_damage_vtable(report, label, name):
    vtable_ea = ida_name.get_name_ea(ida_idaapi.BADADDR, name)
    emit(report, "{}: {}".format(label, name))
    if vtable_ea == ida_idaapi.BADADDR:
        emit(report, "  Vtable name was not found.")
        return

    emit(report, "  Vtable address: {}".format(ea_text(vtable_ea)))
    for index in range(16):
        entry_ea = vtable_ea + index * 4
        target_ea = idc.get_wide_dword(entry_ea)
        function = containing_function(target_ea)
        if function is None:
            if index == 0:
                emit(
                    report,
                    "  Entry 0: {} ({})".format(ea_text(target_ea), symbol_at(target_ea)),
                )
            break
        emit(
            report,
            "  Entry {}: {} {}".format(
                index, ea_text(target_ea), function_text(function)
            ),
        )


def report_vtable_refresher(report, label, name, hexrays_ready):
    vtable_ea = ida_name.get_name_ea(ida_idaapi.BADADDR, name)
    emit(report, "{} refresh: {}".format(label, name))
    if vtable_ea == ida_idaapi.BADADDR:
        emit(report, "  Vtable name was not found.")
        return

    refresh_ea = idc.get_wide_dword(vtable_ea + 4)
    function = containing_function(refresh_ea)
    if function is None:
        emit(report, "  Slot 1 is not a function: {}".format(ea_text(refresh_ea)))
        return

    emit(report, "  Slot 1: {}".format(function_text(function)))
    if not hexrays_ready:
        emit(report, "  Hex-Rays pseudocode: unavailable")
        return

    try:
        cfunc = ida_hexrays.decompile(function.start_ea)
        if cfunc is None:
            emit(report, "  <decompilation returned no function>")
            return
        for line in cfunc.get_pseudocode():
            emit(report, "  " + ida_lines.tag_remove(line.line))
    except Exception as error:
        emit(report, "  Hex-Rays failed: {}".format(error))


def report_concrete_damage_refreshers(report, hexrays_ready):
    vtables = []
    for vtable_ea, name in idautils.Names():
        if not name.startswith("??_7CTelemetryDamage") or "@@6B@" not in name:
            continue
        vtables.append((vtable_ea, name))

    for vtable_ea, name in sorted(vtables, key=lambda item: item[1]):
        refresh_ea = idc.get_wide_dword(vtable_ea + 4)
        function = containing_function(refresh_ea)
        emit(report, "{} at {}".format(name, ea_text(vtable_ea)))
        if function is None:
            emit(report, "  Slot 1 is not a function: {}".format(ea_text(refresh_ea)))
            continue
        emit(report, "  Slot 1 refresh: {}".format(function_text(function)))
        if not hexrays_ready:
            continue
        try:
            cfunc = ida_hexrays.decompile(function.start_ea)
            if cfunc is None:
                emit(report, "  <decompilation returned no function>")
                continue
            for line in cfunc.get_pseudocode():
                emit(report, "  " + ida_lines.tag_remove(line.line))
        except Exception as error:
            emit(report, "  Hex-Rays failed: {}".format(error))


def emit(report, line=""):
    report.write(line + "\n")
    report.flush()


def main():
    ida_auto.auto_wait()
    report_dir = os.path.dirname(REPORT_PATH)
    if report_dir:
        os.makedirs(report_dir, exist_ok=True)

    with open(REPORT_PATH, "w", encoding="utf-8") as report:
        emit(report, "RBRHUD IDA static-analysis report")
        emit(report, "Input: " + idc.get_input_file_path())
        emit(report, "Image base: " + ea_text(ida_ida.inf_get_min_ea()))
        emit(report)

        emit(report, "=== Memory API import cross-references ===")
        emit(report, "Enumerating imports...")
        imports = find_imports()
        if not imports:
            emit(report, "No selected imports found.")
        for module_name, import_ea, name, ordinal in imports:
            emit(
                report,
                "{}!{} at {} (ordinal {})".format(
                    module_name, name, ea_text(import_ea), ordinal
                ),
            )
            xrefs = list(idautils.XrefsTo(import_ea))
            if not xrefs:
                emit(report, "  No direct xrefs to import address.")
            for xref in xrefs:
                function = containing_function(xref.frm)
                emit(
                    report,
                    "  xref {} from {} in {}".format(
                        xref.type, ea_text(xref.frm), function_text(function)
                    ),
                )
                for line in instruction_window(xref.frm):
                    emit(report, line)
        emit(report)

        emit(report, "=== Interesting string cross-references ===")
        emit(report, "Enumerating strings...")
        strings = find_interesting_strings()
        if not strings:
            emit(report, "No selected strings found.")
        for string_ea, value in strings:
            emit(report, "{}: {}".format(ea_text(string_ea), value))
            xrefs = list(idautils.XrefsTo(string_ea))
            if not xrefs:
                emit(report, "  No xrefs.")
            for xref in xrefs:
                function = containing_function(xref.frm)
                emit(
                    report,
                    "  xref {} from {} in {}".format(
                        xref.type, ea_text(xref.frm), function_text(function)
                    ),
                )
                if function is not None:
                    for line in instruction_window(xref.frm):
                        emit(report, line)
                else:
                    for line in data_window(xref.frm):
                        emit(report, line)
        emit(report)

        emit(report, "=== Named CTelemetryDamage artifacts ===")
        emit(report, "Enumerating IDA names...")
        named = [
            (ea, name)
            for ea, name in idautils.Names()
            if "CTelemetryDamage" in name
        ]
        if not named:
            emit(report, "IDA did not assign names to CTelemetryDamage artifacts.")
        for ea, name in named:
            emit(report, "{}: {}".format(ea_text(ea), name))
            for xref in idautils.XrefsTo(ea):
                emit(
                    report,
                    "  xref {} from {} in {}".format(
                        xref.type,
                        ea_text(xref.frm),
                        function_text(containing_function(xref.frm)),
                    ),
                )
        emit(report)

        emit(report, "=== Focused damage telemetry function analysis ===")
        hexrays_ready = (
            ida_hexrays is not None and ida_hexrays.init_hexrays_plugin()
        )
        emit(
            report,
            "Hex-Rays available: {}".format("yes" if hexrays_ready else "no"),
        )
        for label, start_ea in TARGET_FUNCTIONS:
            report_target_function(report, label, start_ea, hexrays_ready)
            emit(report)

        emit(report, "=== Representative damage telemetry vtables ===")
        for label, name in DAMAGE_VTABLES:
            report_damage_vtable(report, label, name)
            emit(report)

        emit(report, "=== Runtime warning telemetry refreshers ===")
        for label, name in WARNING_TELEMETRY_VTABLES:
            report_damage_vtable(report, label, name)
            report_vtable_refresher(report, label, name, hexrays_ready)
            emit(report)

        emit(report, "=== Concrete damage telemetry refresh overrides ===")
        report_concrete_damage_refreshers(report, hexrays_ready)

    print("RBRHUD IDA report written to {}".format(REPORT_PATH))


try:
    main()
except Exception:
    traceback.print_exc()
    raise
finally:
    ida_pro.qexit(0)
