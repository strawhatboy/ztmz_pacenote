"""Capture a configured DR2 UDP destination without changing game settings."""
import argparse
import json
import socket
import struct
import time
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--port', type=int, default=59996)
p.add_argument('--seconds', type=int, default=1800)
p.add_argument('--output', required=True)
a = p.parse_args()
target = Path(a.output)
target.parent.mkdir(parents=True, exist_ok=True)
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.bind(('127.0.0.1', a.port))
sock.settimeout(1)
print(f'Listening on 127.0.0.1:{a.port}', flush=True)
deadline = time.monotonic() + a.seconds
last = None
with target.open('x', encoding='utf-8') as output:
    while time.monotonic() < deadline:
        try:
            packet, remote = sock.recvfrom(4096)
        except socket.timeout:
            continue
        if len(packet) < 264:
            print('Short packet', len(packet), flush=True)
            continue
        values = struct.unpack('<66f', packet[:264])
        output.write(json.dumps({'at': time.time(), 'values': values}) + '\n')
        output.flush()
        summary = tuple(round(values[i], 1) for i in (0, 1, 2, 3, 63, 64, 65))
        if summary != last:
            print(summary, flush=True)
            last = summary
