import importlib.util
from pathlib import Path
import struct
import unittest

spec = importlib.util.spec_from_file_location('wrc7_capture', Path(__file__).with_name('capture-results.py'))
capture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(capture)


class CaptureTests(unittest.TestCase):
    def setUp(self):
        self.base = 0x140000000
        self.memory = {}
        for address, value in ((self.base + 0x1091b80, 0x20000), (0x20000, self.base + 0xdb1b60),
                               (0x20000 + 0x17798, 0x40000), (0x20000 + 0x177a0, 0x40170),
                               (0x200b0, 0x50000), (0x200b8, 0x50008), (0x50000, 0x60000),
                               (0x60038, 0x70000), (0x70908, 0x60000)):
            self.memory[address] = struct.pack('<Q', value)
        self.memory[0x708c8] = bytes([0, 0, 0])
        self.memory[0x40154] = struct.pack('<iiiBB', 0, 0, 0, 0, 0)
        self.memory[0x7006c] = struct.pack('<i', 10000)
        self.memory[0x7092c] = struct.pack('<i', 0)

    def read(self, address, size):
        value = self.memory[address]
        self.assertEqual(size, len(value))
        return value

    def test_records_zero_time_failure_flags(self):
        self.memory[0x708c8] = bytes([1, 0, 1])
        self.memory[0x40154] = struct.pack('<iiiBB', 0, 0, 0, 1, 1)
        result = capture.sample(self.read, self.base)
        self.assertEqual([1, 0, 1], result['statsFlags8c8To8ca'])
        self.assertEqual([1, 1], result['profileFlags160To161'])

    def test_rejects_unverified_multiplayer_profile_selection(self):
        self.memory[0x20000 + 0x177a0] = struct.pack('<Q', 0x402e0)
        with self.assertRaises(OSError):
            capture.sample(self.read, self.base)

    def test_rejects_unowned_stats(self):
        self.memory[0x70908] = struct.pack('<Q', 0x60008)
        with self.assertRaises(OSError):
            capture.sample(self.read, self.base)

    def test_rejects_mid_read_result_change(self):
        count = 0
        def changing(address, size):
            nonlocal count
            if address == 0x40154:
                count += 1
                if count == 2:
                    return struct.pack('<iiiBB', 0, 0, 0, 1, 1)
            return self.read(address, size)
        with self.assertRaises(OSError):
            capture.sample(changing, self.base)


if __name__ == '__main__':
    unittest.main()
