import importlib.util
from pathlib import Path
import struct
import unittest

spec = importlib.util.spec_from_file_location('capture_results', Path(__file__).with_name('capture-results.py'))
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)


class ResultCaptureTests(unittest.TestCase):
    def setUp(self):
        self.memory = {}
        self.base, self.manager, self.race = 0x140000000, 0x200000, 0x300000
        self.profile, self.timer, self.participant = 0x400000, 0x500000, 0x600000
        for address, value in ((self.base + 0x1c61660, self.manager),
                               (self.manager + 0x48, self.race),
                               (self.base + 0x1d61938, self.race),
                               (self.manager + 0x1378, self.profile),
                               (self.manager + 0xd50, 0x700000),
                               (self.race + 0x4e8, self.timer),
                               (self.timer + 0xa0, self.participant),
                               (self.participant + 0x38, self.timer)):
            self.memory[address] = struct.pack('<Q', value)
        self.memory[self.manager + 0x1374] = struct.pack('<i', 1)
        self.memory[self.manager + 0x1380] = b'\0'
        self.memory[self.manager + 0x12d0] = struct.pack('<i', 1)
        self.memory[self.manager + 0x12e0] = struct.pack('<i', 0)
        self.memory[self.profile + 0x19c] = struct.pack('<iiBB', 0, 0, 0, 0)
        self.memory[self.timer + 0x9c] = bytes([0, 0, 0, 0])

    def read(self, address, size):
        data = self.memory[address]
        self.assertEqual(size, len(data))
        return data

    def test_captures_failed_result_even_when_zero_times(self):
        self.memory[self.profile + 0x19c] = struct.pack('<iiBB', 0, 0, 1, 1)
        self.memory[self.timer + 0x9c] = bytes([0, 0, 0, 1])
        result = probe.sample(self.read, self.base)
        self.assertEqual(1, result['profileFlag1a5'])
        self.assertEqual([0, 0, 0, 1], result['timerFlags9cTo9f'])

    def test_out_of_bounds_player_is_unavailable(self):
        self.memory[self.manager + 0x1380] = bytes([1])
        self.assertFalse(probe.sample(self.read, self.base)['available'])

    def test_rejects_wrong_participant_owner(self):
        self.memory[self.participant + 0x38] = struct.pack('<Q', self.timer + 8)
        with self.assertRaises(OSError):
            probe.sample(self.read, self.base)

    def test_rejects_result_changing_mid_read(self):
        seen = 0
        def changing(address, size):
            nonlocal seen
            if address == self.profile + 0x19c:
                seen += 1
                if seen == 2:
                    return struct.pack('<iiBB', 0, 0, 1, 1)
            return self.read(address, size)
        with self.assertRaises(OSError):
            probe.sample(changing, self.base)


if __name__ == '__main__':
    unittest.main()
