/* ==========================================================================
   chars.js —— 角色包管理与导入
   一个角色包 = 一个文件夹（网页端用 IndexedDB 存储）：
     char.json    角色元信息（name / 台词 / 语音引用 / 尺寸）
     frames/*.webp  动画帧
     voices/*.mp3   语音
   导入流程：选 zip 或文件夹 → 解析 → 校验 → 转成网页版结构 → 存入 IndexedDB
   ========================================================================== */
(function (global) {
  'use strict';

  const DB_NAME = 'qpet.chars';
  const STORE = 'packs';

  /* ---------------- IndexedDB 存储 ---------------- */
  function openDB() {
    return new Promise((res, rej) => {
      const req = indexedDB.open(DB_NAME, 1);
      req.onupgradeneeded = () => {
        const db = req.result;
        if (!db.objectStoreNames.contains(STORE)) db.createObjectStore(STORE);
      };
      req.onsuccess = () => res(req.result);
      req.onerror = () => rej(req.error);
    });
  }

  async function dbPut(key, value) {
    const db = await openDB();
    return new Promise((res, rej) => {
      const tx = db.transaction(STORE, 'readwrite');
      tx.objectStore(STORE).put(value, key);
      tx.oncomplete = () => res();
      tx.onerror = () => rej(tx.error);
    });
  }

  async function dbGet(key) {
    const db = await openDB();
    return new Promise((res, rej) => {
      const tx = db.transaction(STORE, 'readonly');
      const r = tx.objectStore(STORE).get(key);
      r.onsuccess = () => res(r.result);
      r.onerror = () => rej(r.error);
    });
  }

  async function dbKeys() {
    const db = await openDB();
    return new Promise((res, rej) => {
      const tx = db.transaction(STORE, 'readonly');
      const r = tx.objectStore(STORE).getAllKeys();
      r.onsuccess = () => res(r.result);
      r.onerror = () => rej(r.error);
    });
  }

  async function dbDelete(key) {
    const db = await openDB();
    return new Promise((res, rej) => {
      const tx = db.transaction(STORE, 'readwrite');
      tx.objectStore(STORE).delete(key);
      tx.oncomplete = () => res();
      tx.onerror = () => rej(tx.error);
    });
  }

  /* ---------------- zip 解压（用 DecompressionStream，纯浏览器原生） ---------------- */
  async function unzip(buf) {
    // 解析 zip 中央目录
    const dv = new DataView(buf);
    const bytes = new Uint8Array(buf);
    // 找 EOCD
    let eocd = -1;
    for (let i = buf.byteLength - 22; i >= 0 && i > buf.byteLength - 66000; i--) {
      if (dv.getUint32(i, true) === 0x06054b50) { eocd = i; break; }
    }
    if (eocd < 0) throw new Error('不是有效的 zip 文件');

    const count = dv.getUint16(eocd + 10, true);
    let off = dv.getUint32(eocd + 16, true);
    const out = {};

    for (let n = 0; n < count; n++) {
      if (dv.getUint32(off, true) !== 0x02014b50) break;
      const method = dv.getUint16(off + 10, true);
      const compSize = dv.getUint32(off + 20, true);
      const nameLen = dv.getUint16(off + 28, true);
      const extraLen = dv.getUint16(off + 30, true);
      const commentLen = dv.getUint16(off + 32, true);
      const localOff = dv.getUint32(off + 42, true);
      const name = new TextDecoder().decode(bytes.subarray(off + 46, off + 46 + nameLen));
      off += 46 + nameLen + extraLen + commentLen;

      if (name.endsWith('/')) continue;             // 目录
      if (name.includes('__MACOSX')) continue;      // macOS 垃圾目录

      // 读本地头拿真实数据起点
      const lNameLen = dv.getUint16(localOff + 26, true);
      const lExtraLen = dv.getUint16(localOff + 28, true);
      const dataStart = localOff + 30 + lNameLen + lExtraLen;
      const raw = bytes.subarray(dataStart, dataStart + compSize);

      let content;
      if (method === 0) {
        content = raw;                              // 已压缩(store)
      } else if (method === 8) {
        const ds = new DecompressionStream('deflate-raw');
        const stream = new Blob([raw]).stream().pipeThrough(ds);
        content = new Uint8Array(await new Response(stream).arrayBuffer());
      } else {
        continue;                                    // 不支持的压缩方式
      }
      out[name] = content;
    }
    return out;
  }

  /* ---------------- 角色包构建 ---------------- */
  /**
   * 从一组文件（{相对路径: Uint8Array}）构建角色包。
   * 兼容两种来源：
   *  A. 已经是网页版结构（frames/*.webp + voices/*.mp3 + char.json）
   *  B. 桌面版结构（动画/frames.bin + 音效/voices/*.wav + 台词.txt）
   *    —— B 需要服务端转换，网页端只做 A，导入时给出明确提示。
   */
  async function buildPack(id, files) {
    const pack = { id, name: id, frames: [], voices: [], char: null, blobs: {} };

    // 找 char.json
    for (const path in files) {
      if (path.endsWith('char.json')) {
        const text = new TextDecoder().decode(files[path]);
        const meta = {};
        for (const line of text.split('\n')) {
          if (line.includes('=')) {
            const k = line.slice(0, line.indexOf('=')).trim();
            const v = line.slice(line.indexOf('=') + 1).trim();
            meta[k] = v;
          }
        }
        pack.char = meta;
        pack.name = meta.name || id;
        break;
      }
    }

    // 收集帧
    const framePaths = Object.keys(files)
      .filter(p => /\.(webp|png|jpg)$/i.test(p) && /frame|动画/i.test(p))
      .sort();
    if (!framePaths.length) {
      throw new Error('没找到动画帧。网页版需要 .webp 帧（可用仓库里的转换脚本从桌面版角色包生成）');
    }
    for (const p of framePaths) {
      const name = p.split('/').pop();
      pack.blobs['frames/' + name] = files[p];
      pack.frames.push({ file: 'frames/' + name });
    }

    // 收集语音
    const voicePaths = Object.keys(files)
      .filter(p => /\.(mp3|m4a|ogg|wav)$/i.test(p))
      .sort();
    for (const p of voicePaths) {
      const name = p.split('/').pop();
      pack.blobs['voices/' + name] = files[p];
      pack.voices.push({ text: '', file: 'voices/' + name });
    }

    // 台词表：voices.tsv（文本\t路径）
    for (const path in files) {
      if (path.endsWith('voices.tsv') || path.endsWith('台词.txt')) {
        const text = new TextDecoder().decode(files[path]);
        const lines = text.split('\n').filter(l => l.trim());
        let i = 0;
        for (const line of lines) {
          const [t, rel] = line.includes('\t') ? line.split('\t') : [line, ''];
          const base = (rel.split('/').pop() || '').replace(/\.[^.]+$/, '');
          const found = pack.voices.find(v => v.file.split('/').pop().replace(/\.[^.]+$/, '') === base);
          if (found) { found.text = t.trim(); i++; }
        }
        if (i) break;
      }
    }

    return pack;
  }

  /* ---------------- 存取 ---------------- */
  async function savePack(pack) {
    await dbPut(pack.id, pack);
  }

  async function loadPack(id) {
    return await dbGet(id);
  }

  async function listPacks() {
    return await dbKeys();
  }

  async function removePack(id) {
    await dbDelete(id);
  }

  /**
   * 取一个资源的 blob URL（优先用内置资源，回退到用户导入的包）
   */
  function makeResolver(builtInBase) {
    const cache = new Map();
    return async function resolve(pack, relPath) {
      const key = pack.id + '/' + relPath;
      if (cache.has(key)) return cache.get(key);
      let url = null;
      if (pack.builtin) {
        url = builtInBase + '/' + pack.id + '/' + relPath;
      } else {
        const blob = pack.blobs && pack.blobs[relPath];
        if (blob) url = URL.createObjectURL(new Blob([blob]));
      }
      cache.set(key, url);
      return url;
    };
  }

  global.QPet = global.QPet || {};
  global.QPet.CharStore = {
    savePack, loadPack, listPacks, removePack, buildPack, unzip, makeResolver,
  };
})(window);
