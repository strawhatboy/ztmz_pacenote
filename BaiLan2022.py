import cv2
from pathlib import Path
import numpy as np
import tqdm

OUTPUT = Path('output')
OUTPUT.mkdir(exist_ok=True)

def get_frames():
    cap= cv2.VideoCapture('bailan.mp4')
    width  = cap.get(cv2.CAP_PROP_FRAME_WIDTH)   # float `width`
    height = cap.get(cv2.CAP_PROP_FRAME_HEIGHT)  # float `height`

    print('width, height:', width, height)
    
    fps = cap.get(cv2.CAP_PROP_FPS)
    
    print('fps:', fps)  # float `fps`
    
    frame_count = cap.get(cv2.CAP_PROP_FRAME_COUNT)
    
    print('frames count:', frame_count)  # float `frame_count`
    i=0
    while(cap.isOpened()):
        ret, frame = cap.read()

        if ret == False:
            break
        scale_percent = 20 # percent of original size
        width = int(frame.shape[1] * scale_percent / 100)
        height = int(frame.shape[0] * scale_percent / 100)
        dim = (width, height)
        resized = cv2.resize(frame, dim, interpolation = cv2.INTER_AREA)
        cannied = cv2.Canny(resized, 100, 200)
        inverted = cv2.bitwise_not(cannied)

        # background to transparent
        rgba = cv2.cvtColor(inverted, cv2.COLOR_RGB2RGBA)
        b,g,r,a = cv2.split(rgba)
        r[(cannied == 255)] = 255
        dst = cv2.merge((b, g, r, cannied))
        
        cv2.imwrite(str(OUTPUT / f'{str(i)}.png'), dst)
        i+=1

def main():
    get_frames();
    pass

if __name__ == '__main__':
    main()
