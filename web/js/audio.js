/* ==========================================================================
   audio.js —— 语音引擎
   桌面端用 Win32 MCI / PlaySound 播放 wav；网页端改用 Web Audio API：
     · decodeAudioData 缓存解码结果
     · 音量用 GainNode 控制（对应桌面的音量设置）
     · 台词池支持「随机」与「顺序」两种模式
   ========================================================================== */
(function (global) {
  'use strict';

  class VoiceEngine {
    constructor() {
      this.ctx = null;
      this.gain = null;
      this.buffers = new Map();     // url -> AudioBuffer
      this.volume = 1.0;
      this.current = null;
      this.lines = [];              // [{text, file}]
      this.mode = 'random';         // random | sequential
      this.seqIndex = 0;
      this.onPlay = null;           // (line, durationSec) => void
      this._pending = null;
    }

    /** 首次用户交互后才能创建 AudioContext（浏览器自动播放策略） */
    ensureContext() {
      if (!this.ctx) {
        const AC = global.AudioContext || global.webkitAudioContext;
        if (!AC) return null;
        this.ctx = new AC();
        this.gain = this.ctx.createGain();
        this.gain.gain.value = this.volume;
        this.gain.connect(this.ctx.destination);
      }
      if (this.ctx.state === 'suspended') this.ctx.resume();
      return this.ctx;
    }

    setVolume(v) {
      this.volume = Math.max(0, Math.min(1, v));
      if (this.gain) this.gain.gain.value = this.volume;
    }

    setLines(lines) {
      this.lines = lines || [];
      this.seqIndex = 0;
    }

    setMode(m) { this.mode = m; this.seqIndex = 0; }

    /** 预加载并解码全部语音 */
    async preload(base) {
      const ctx = this.ensureContext();
      if (!ctx) return;
      for (const l of this.lines) {
        if (this.buffers.has(l.file)) continue;
        try {
          const res = await fetch(base + '/' + l.file);
          if (!res.ok) continue;
          const arr = await res.arrayBuffer();
          const buf = await ctx.decodeAudioData(arr);
          this.buffers.set(l.file, buf);
        } catch (e) {
          /* 单条失败不阻断 */
        }
      }
    }

    /** 下一条台词（按模式） */
    next() {
      if (!this.lines.length) return null;
      if (this.mode === 'sequential') {
        const l = this.lines[this.seqIndex % this.lines.length];
        this.seqIndex++;
        return l;
      }
      return this.lines[Math.floor(Math.random() * this.lines.length)];
    }

    /** 播放一条（异步加载 → 播放） */
    async play(line) {
      const ctx = this.ensureContext();
      if (!ctx || !line) return;
      try {
        let buf = this.buffers.get(line.file);
        if (!buf) {
          const res = await fetch(line.file);
          if (!res.ok) return;
          const arr = await res.arrayBuffer();
          buf = await ctx.decodeAudioData(arr);
          this.buffers.set(line.file, buf);
        }
        this.stop();
        const src = ctx.createBufferSource();
        src.buffer = buf;
        src.connect(this.gain);
        src.onended = () => { if (this.current === src) this.current = null; };
        src.start();
        this.current = src;
        if (this.onPlay) this.onPlay(line, buf.duration);
        return buf.duration;
      } catch (e) {
        /* 忽略：播放失败不该打断交互 */
      }
    }

    /** 播放下一条并返回该台词文本 */
    async playNext() {
      const l = this.next();
      if (!l) return null;
      await this.play(l);
      return l.text;
    }

    stop() {
      if (this.current) {
        try { this.current.stop(); } catch (e) { /* 已停止 */ }
        this.current = null;
      }
    }

    get playing() { return this.current !== null; }
  }

  global.QPet = global.QPet || {};
  global.QPet.VoiceEngine = VoiceEngine;
})(window);
