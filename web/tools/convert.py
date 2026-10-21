"""资源转换管线：桌面端角色包 → 网页版资源。

frames.bin（裸 BGRA）→ 逐帧 WebP
wav → mp3
char.json → 网页版 char.json（补 web 字段）

用法：python web/tools/convert.py [角色名...]
不传参数则转换全部角色。
"""
import io, json, os, struct, subprocess, sys, zlib, shutil

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, 'chars')
DST = os.path.join(ROOT, 'web', 'assets', 'chars')
FFMPEG = shutil.which('ffmpeg') or r'D:\ffmpeg-7.1-essentials_build\bin\ffmpeg.exe'

# 帧序列命名：四组动画在 frames.bin 里是连续排列的
#   [0, idle)        待机
#   [idle, idle+press)   按下
#   [+click)        点击
#   [+drag)         拖动
ANIM = ['idle', 'press', 'click', 'drag']


def png_rgba(w, h, rgba):
    """把 RGBA 像素包成 PNG 字节流（纯标准库）"""
    raw = bytearray()
    for y in range(h):
        raw.append(0)
        raw += rgba[y*w*4:(y+1)*w*4]
    comp = zlib.compress(bytes(raw), 6)

    def ck(t, d):
        return struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t+d) & 0xffffffff)

    return (b'\x89PNG\r\n\x1a\n'
            + ck(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
            + ck(b'IDAT', comp)
            + ck(b'IEND', b''))


def to_webp(src_png, dst_webp, q=80):
    r = subprocess.run([FFMPEG, '-hide_banner', '-loglevel', 'error', '-y',
                        '-i', src_png, '-c:v', 'libwebp', '-q:v', str(q), dst_webp],
                       capture_output=True)
    return r.returncode == 0 and os.path.exists(dst_webp) and os.path.getsize(dst_webp) > 0


def convert_char(name):
    sp = os.path.join(SRC, name)
    if not os.path.isdir(sp):
        print('  跳过（不存在）')
        return None

    fj = os.path.join(sp, '动画', 'frames.json')
    fb = os.path.join(sp, '动画', 'frames.bin')
    if not (os.path.exists(fj) and os.path.exists(fb)):
        print('  跳过（无动画帧）')
        return None

    meta = json.load(io.open(fj, encoding='utf-8'))
    w, h = meta['w'], meta['h']
    counts = {k: meta.get(k, 0) for k in ANIM}
    total = sum(counts.values())

    dp = os.path.join(DST, name)
    os.makedirs(os.path.join(dp, 'frames'), exist_ok=True)
    os.makedirs(os.path.join(dp, 'voices'), exist_ok=True)

    blob = io.open(fb, 'rb').read()
    need = w * h * 4 * total
    if len(blob) < need:
        print('  ✗ 帧文件不足')
        return None

    # 逐帧导出：BGRA → RGBA → PNG → WebP
    tmp_png = os.path.join(dp, '_tmp.png')
    frames = []
    idx = 0
    for anim in ANIM:
        n = counts[anim]
        for i in range(n):
            off = idx * w * h * 4
            src = blob[off:off + w*h*4]
            rgba = bytearray(w*h*4)
            for p in range(w*h):
                # 源是 BGRA
                rgba[p*4]   = src[p*4+2]
                rgba[p*4+1] = src[p*4+1]
                rgba[p*4+2] = src[p*4]
                rgba[p*4+3] = src[p*4+3]
            io.open(tmp_png, 'wb').write(png_rgba(w, h, rgba))
            out = os.path.join(dp, 'frames', f'{anim}_{i:03d}.webp')
            if not to_webp(tmp_png, out, 80):
                print('  ✗ WebP 编码失败')
                return None
            frames.append({'anim': anim, 'i': i, 'file': f'frames/{anim}_{i:03d}.webp'})
            idx += 1
    if os.path.exists(tmp_png):
        os.remove(tmp_png)

    # 语音：wav → mp3
    voices = []
    vp = os.path.join(sp, '音效', 'voices')
    tsv = os.path.join(sp, '音效', 'voices.tsv')
    lines = []
    if os.path.exists(tsv):
        for ln in io.open(tsv, encoding='utf-8-sig').read().split('\n'):
            if '\t' in ln:
                t, p = ln.split('\t', 1)
                lines.append((t.strip(), p.strip()))
    for text, rel in lines:
        src_wav = os.path.join(sp, rel.replace('/', os.sep))
        if not os.path.exists(src_wav):
            continue
        base = os.path.basename(rel).rsplit('.', 1)[0]
        out_mp3 = os.path.join(dp, 'voices', base + '.mp3')
        r = subprocess.run([FFMPEG, '-hide_banner', '-loglevel', 'error', '-y',
                            '-i', src_wav, '-codec:a', 'libmp3lame', '-b:a', '96k', out_mp3],
                           capture_output=True)
        if r.returncode == 0 and os.path.exists(out_mp3):
            voices.append({'text': text, 'file': 'voices/' + base + '.mp3'})

    # char.json（网页版）
    cj = {}
    cjp = os.path.join(sp, 'char.json')
    if os.path.exists(cjp):
        for ln in io.open(cjp, encoding='utf-8-sig').read().split('\n'):
            if '=' in ln:
                k, v = ln.split('=', 1)
                cj[k.strip()] = v.strip()

    web_char = {
        'name': cj.get('name', name),
        'w': w, 'h': h,
        'fps': meta.get('fps', 30),
        'counts': counts,
        'frames': frames,
        'voices': voices,
        'charH': int(cj.get('charH', h) or h),
        'press': cj.get('press', ''),
        'release': cj.get('release', ''),
        'amp': {k: float(cj.get('amp'+k.capitalize(), '1.0') or 1.0) for k in ANIM},
    }
    io.open(os.path.join(dp, 'char.json'), 'w', encoding='utf-8').write(
        json.dumps(web_char, ensure_ascii=False, indent=1))

    # 静态立绘（首帧用 WebP 即可，这里复用 image.png 转换）
    img = os.path.join(sp, '图片', 'image.png')
    if os.path.exists(img):
        out = os.path.join(dp, 'portrait.webp')
        subprocess.run([FFMPEG, '-hide_banner', '-loglevel', 'error', '-y',
                        '-i', img, '-c:v', 'libwebp', '-q:v', '88', out], capture_output=True)

    size = sum(os.path.getsize(os.path.join(dp, f))
               for f in os.listdir(dp) if os.path.isfile(os.path.join(dp, f)))
    for sub in ('frames', 'voices'):
        d2 = os.path.join(dp, sub)
        if os.path.isdir(d2):
            size += sum(os.path.getsize(os.path.join(d2, f)) for f in os.listdir(d2))
    print(f'  ✓ {name}: {total} 帧 + {len(voices)} 语音 → {size/1048576:.2f} MB')
    return web_char


def main():
    names = sys.argv[1:]
    if not names:
        names = sorted(d for d in os.listdir(SRC) if os.path.isdir(os.path.join(SRC, d)))
    print('转换资源（ffmpeg: %s）' % FFMPEG)
    ok = []
    for n in names:
        print('· %s' % n)
        if convert_char(n):
            ok.append(n)
    # 总索引
    idx = {'chars': []}
    for n in ok:
        p = os.path.join(DST, n, 'char.json')
        if os.path.exists(p):
            c = json.load(io.open(p, encoding='utf-8'))
            idx['chars'].append({
                'id': n, 'name': c['name'], 'w': c['w'], 'h': c['h'],
                'voiceCount': len(c['voices']),
            })
    io.open(os.path.join(DST, 'index.json'), 'w', encoding='utf-8').write(
        json.dumps(idx, ensure_ascii=False, indent=1))
    print('\n索引写入 %d 个角色' % len(idx['chars']))


if __name__ == '__main__':
    main()