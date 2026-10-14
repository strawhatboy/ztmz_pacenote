import importlib.util
from pathlib import Path
import struct
import unittest

spec = importlib.util.spec_from_file_location('capture_race', Path(__file__).with_name('capture-race.py'))
capture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(capture)


class CaptureTests(unittest.TestCase):
    def setUp(self):
        self.base = 0x140000000
        self.memory = {}
        for address, value in ((self.base + 0xb4ef3d8, 0x20000), (0x20228, 0x30000),
                               (0x30038, 0x40000), (0x40000, 0x50000), (0x50030, 0x60000),
                               (0x602b0, 0x70000), (0x80000, 0x90000),
                               (0x90000, self.base + 0x9dd8c98), (0x900a0, 0x60000),
                               (0x90200, 0xa0000), (0xa0000, self.base + 0x9dd5da0),
                               (0xa00a0, 0x70000)):
            self.put(address, '<Q', value)
        self.put(0x30040, '<i', 1)
        self.put(0x60280, '<Qii', 0, 0, 0)
        self.put(0x60290, '<Qii', 0x80000, 1, 1)
        self.put(0xa0110, '<i', 2)
        self.put(0xa0118, '<ff', 12.5, 0)
        self.put(0xa0120, '<i', 6)

    def put(self, address, fmt, *values):
        self.memory[address] = struct.pack(fmt, *values)

    def read(self, address, size):
        value = self.memory[address]
        self.assertEqual(size, len(value))
        return value

    def test_reads_native_phases_without_inference(self):
        for phase in (4, 6, 10, 11, 12):
            self.put(0xa0120, '<i', phase)
            result = capture.sample(self.read, self.base)
            self.assertEqual(phase, result['phase'])
            self.assertEqual(12.5, result['raceTime'])

    def test_rejects_foreign_owner_and_replay_manager(self):
        for address, replacement in ((0x900a0, 0xb0000), (0xa00a0, 0xb0000),
                                      (0x90000, self.base + 0x9dd94f8)):
            original = self.memory[address]
            self.put(address, '<Q', replacement)
            with self.assertRaises(OSError):
                capture.sample(self.read, self.base)
            self.memory[address] = original

    def test_rejects_invalid_arrays_players_and_phase(self):
        for address, fmt, values in ((0x60290, '<Qii', (0x80000, 2, 1)),
                                     (0x30040, '<i', (2,)), (0xa0120, '<i', (13,)),
                                     (0xa0118, '<ff', (float('nan'), 0))):
            original = self.memory[address]
            self.put(address, fmt, *values)
            with self.assertRaises(OSError):
                capture.sample(self.read, self.base)
            self.memory[address] = original

    def test_rejects_scene_or_phase_change_during_read(self):
        for target, changed in ((self.base + 0xb4ef3d8, struct.pack('<Q', 0xb0000)),
                                (0xa0120, struct.pack('<i', 11))):
            count = 0
            def read(address, size):
                nonlocal count
                if address == target:
                    count += 1
                    if count == 2:
                        return changed
                return self.read(address, size)
            with self.assertRaises(OSError):
                capture.sample(read, self.base)


if __name__ == '__main__':
    unittest.main()
