"""Read and write straight-channel 16-bit RGBA PNGs for a URP mask atlas."""
import struct
import sys
import zlib
from array import array


def read_rgba16(path):
    data = path.read_bytes()
    assert data[:8] == b'\x89PNG\r\n\x1a\n'
    offset, packed = 8, []
    while offset < len(data):
        length = struct.unpack('>I', data[offset:offset + 4])[0]
        kind = data[offset + 4:offset + 8]
        payload = data[offset + 8:offset + 8 + length]
        if kind == b'IHDR':
            width, height, depth, color, compression, filtering, interlace = struct.unpack('>IIBBBBB', payload)
            assert (depth, color, compression, filtering, interlace) == (16, 6, 0, 0, 0)
        elif kind == b'IDAT':
            packed.append(payload)
        elif kind == b'IEND':
            break
        offset += length + 12
    raw = zlib.decompress(b''.join(packed))
    stride = width * 8
    result, previous = bytearray(), bytearray(stride)

    def paeth(a, b, c):
        p = a + b - c
        pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
        return a if pa <= pb and pa <= pc else b if pb <= pc else c

    for y in range(height):
        pos = y * (stride + 1)
        filter_type, current = raw[pos], bytearray(raw[pos + 1:pos + 1 + stride])
        for x in range(stride):
            a = current[x - 8] if x >= 8 else 0
            b = previous[x]
            c = previous[x - 8] if x >= 8 else 0
            current[x] = (current[x] + (0, a, b, (a + b) // 2, paeth(a, b, c))[filter_type]) & 255
        result.extend(current)
        previous = current
    channels = array('H')
    channels.frombytes(result)
    if sys.byteorder == 'little':
        channels.byteswap()
    return width, height, channels


def write_rgba16(path, width, height, channels):
    assert len(channels) == width * height * 4
    ordered = array('H', channels)
    if sys.byteorder == 'little':
        ordered.byteswap()
    raw = ordered.tobytes()
    stride = width * 8
    rows = b''.join(b'\0' + raw[y * stride:(y + 1) * stride] for y in range(height))

    def chunk(kind, payload):
        return struct.pack('>I', len(payload)) + kind + payload + struct.pack('>I', zlib.crc32(kind + payload) & 0xffffffff)

    header = struct.pack('>IIBBBBB', width, height, 16, 6, 0, 0, 0)
    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', header) + chunk(b'IDAT', zlib.compress(rows)) + chunk(b'IEND', b''))


def pack_metal_smoothness(directory):
    width, height, rough = read_rgba16(directory / 'atlas_roughness.png')
    mw, mh, metal = read_rgba16(directory / 'atlas_metallic.png')
    assert (width, height) == (mw, mh)
    mask = array('H', [0]) * (width * height * 4)
    for i in range(0, len(mask), 4):
        mask[i] = metal[i]
        mask[i + 1] = 65535
        mask[i + 3] = 65535 - rough[i]
    write_rgba16(directory / 'atlas_metallic_smoothness.png', width, height, mask)
