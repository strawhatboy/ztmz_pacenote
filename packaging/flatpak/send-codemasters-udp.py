#!/usr/bin/env python3

import argparse
import socket
import struct
import time


PACKET_SIZE = 264


def write_float(packet: bytearray, offset: int, value: float) -> None:
    struct.pack_into("<f", packet, offset, value)


def create_packet(sequence: int) -> bytes:
    packet = bytearray(PACKET_SIZE)
    elapsed = 12.5 + sequence * 0.05
    stage_distance = 1234.5 + sequence * 1.75

    write_float(packet, 0, elapsed)
    write_float(packet, 4, elapsed)
    write_float(packet, 8, stage_distance)
    write_float(packet, 12, stage_distance / 5000.0)
    write_float(packet, 16, 100.0 + sequence)
    write_float(packet, 20, 5.0)
    write_float(packet, 24, 200.0 + sequence)
    write_float(packet, 28, 34.5)  # 124.2 km/h
    write_float(packet, 29 << 2, 0.75)  # throttle
    write_float(packet, 30 << 2, 0.1)  # steering
    write_float(packet, 31 << 2, 0.0)  # brake
    write_float(packet, 32 << 2, 0.0)  # clutch
    write_float(packet, 33 << 2, 4.0)  # gear
    write_float(packet, 37 << 2, 620.0)  # 6200 RPM
    write_float(packet, 59 << 2, 0.0)  # completed laps
    write_float(packet, 60 << 2, 1.0)  # total laps
    write_float(packet, 63 << 2, 800.0)  # 8000 maximum RPM
    write_float(packet, 64 << 2, 90.0)  # 900 idle RPM
    write_float(packet, 65 << 2, 6.0)  # maximum gears
    write_float(packet, 244, 5000.0)  # track length
    return bytes(packet)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Send simulated DiRT Rally 2.0 UDP telemetry."
    )
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=30000)
    parser.add_argument("--count", type=int, default=20)
    parser.add_argument("--interval-ms", type=float, default=50.0)
    args = parser.parse_args()
    if not 1 <= args.port <= 65535:
        parser.error("--port must be between 1 and 65535")
    if not 1 <= args.count <= 10000:
        parser.error("--count must be between 1 and 10000")
    if not 0 <= args.interval_ms <= 60000:
        parser.error("--interval-ms must be between 0 and 60000")
    return args


def main() -> None:
    args = parse_args()
    destination = (args.host, args.port)
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sender:
        for sequence in range(args.count):
            sender.sendto(create_packet(sequence), destination)
            if sequence + 1 < args.count and args.interval_ms > 0:
                time.sleep(args.interval_ms / 1000.0)

    print(
        f"Sent {args.count} Codemasters packets to "
        f"{args.host}:{args.port} ({PACKET_SIZE} bytes each)."
    )


if __name__ == "__main__":
    main()
