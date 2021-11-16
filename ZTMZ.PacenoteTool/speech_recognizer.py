#!/usr/bin/env python3

from vosk import Model, KaldiRecognizer, SetLogLevel
import sys
import os
import wave
import json
import argparse
from pathlib import Path

SetLogLevel(-1)

if not os.path.exists("speech_model"):
    print ("Please download the model from https://alphacephei.com/vosk/models and unpack as 'model' in the current folder.")
    exit (1)

parser = argparse.ArgumentParser()
parser.add_argument('--framerate', type=int, default=48000)
# parser.add_argument('--distance', type=int, default=0)
args = parser.parse_args()

# load all speeches
speeches = []
with open('aliases.csv') as f:
    # skip first line
    lines = f.readlines()
    lines = lines[1:]
    for line in lines:
        speech = line.split(',')[-1].strip()
        speech = speech.split('|')
        speeches.extend(speech)

model = Model("speech_model")
# rec = KaldiRecognizer(model, args.framerate, '["thirty", "six left", "long", "left five", "into", "right two", "tightens", "don\'t cut", "dont cut", "and", "one thirty", "[unk]"]')
rec = KaldiRecognizer(model, args.framerate, json.dumps(speeches))

while True:
    command = input().split(':')
    if len(command) < 2:
        continue
    distance = command[0]
    filepath = command[1]

    if not Path(filepath).exists():
        continue

    wf = wave.open(filepath, "rb")
# if wf.getnchannels() != 1 or wf.getsampwidth() != 2 or wf.getcomptype() != "NONE":
#     print ("Audio file must be WAV format mono PCM.")
#     exit (1)


# You can also specify the possible word or phrase list as JSON list, the order doesn't have to be strict

    while True:
        data = wf.readframes(4000)
        if len(data) == 0:
            break
        if rec.AcceptWaveform(data):
            # print(rec.Result())
            pass
        else:
            # print(rec.PartialResult())
            pass
    output = '{}>{}'.format(distance, json.loads(rec.FinalResult())["text"])
    print(output)
    # sys.stdout.write(output + os.linesep)
