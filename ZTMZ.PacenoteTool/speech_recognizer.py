#!/usr/bin/env python3

from vosk import Model, KaldiRecognizer, SetLogLevel
import sys
import os
import wave
import json

SetLogLevel(-1)

if not os.path.exists("speech_model"):
    print ("Please download the model from https://alphacephei.com/vosk/models and unpack as 'model' in the current folder.")
    exit (1)

wf = wave.open(sys.argv[1], "rb")
if wf.getnchannels() != 1 or wf.getsampwidth() != 2 or wf.getcomptype() != "NONE":
    print ("Audio file must be WAV format mono PCM.")
    exit (1)

model = Model("speech_model")

# You can also specify the possible word or phrase list as JSON list, the order doesn't have to be strict
rec = KaldiRecognizer(model, wf.getframerate(), '["left five", "into", "right two", "don\'t cut", "dont cut", "and", "one thirty", "[unk]"]')

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

print(json.loads(rec.FinalResult())["text"])