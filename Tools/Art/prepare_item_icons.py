"""Trim rendered transparency so long items use tall inventory cells correctly.
Usage: python prepare_item_icons.py STAGING_DIRECTORY (requires Pillow).
"""
from pathlib import Path
import sys
from PIL import Image

def prepare(directory):
    for path in Path(directory).glob('*.png'):
        image=Image.open(path).convert('RGBA')
        bounds=image.getchannel('A').getbbox()
        if not bounds:
            raise ValueError('Empty render: '+str(path))
        content=image.crop(bounds)
        margin=max(4,round(max(content.size)*.06))
        output=Image.new('RGBA',(content.width+margin*2,content.height+margin*2))
        output.paste(content,(margin,margin))
        output.save(path)
if __name__=='__main__':prepare(sys.argv[1])
