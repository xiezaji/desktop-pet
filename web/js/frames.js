/* ==========================================================================
   frames.js —— 精灵帧播放器
   桌面端 frames.bin 是裸 BGRA 像素数组（w*h*4 一帧，帧序 idle→press→click→drag）。
   网页版改用逐帧 WebP，用 <img> 预解码后按 fps 节拍 drawImage。
   ========================================================================== */
(function (global) {
  'use strict';

  const ANIM = ['idle', 'press', 'click', 'drag'];

  class FrameSet {
    constructor(meta) {
      this.w = meta.w;
      this.h = meta.h;
      this.fps = meta.fps || 30;
      this.counts = meta.counts;
      this.images = {};        // anim -> [Image]
      this.ready = false;
      this.progress = 0;
    }

    get total() {
      return ANIM.reduce((s, k) => s + (this.counts[k] || 0), 0);
    }

    /**
     * 加载全部帧。
     * @param {string} base  资源基址，如 'assets/chars/小维'
     * @param {Array}  frames  转换管线产出的帧清单
     * @param {function} onProgress  (done, total) => void
     */
    async load(base, frames, onProgress) {
      const tasks = [];
      let done = 0;
      const total = frames.length;

      for (const f of frames) {
        const img = new Image();
        img.decoding = 'async';
        const task = new Promise((resolve, reject) => {
          img.onload = () => {
            done++;
            onProgress && onProgress(done, total);
            resolve();
          };
          img.onerror = () => {
            done++;
            onProgress && onProgress(done, total);
            reject(new Error('帧加载失败: ' + f.file));
          };
        });
        img.src = base + '/' + f.file;
        (this.images[f.anim] = this.images[f.anim] || []).push(img);
        tasks.push(task);
      }

      // 单帧失败不阻断整体（少一帧不至于整只角色不显示）
      await Promise.allSettled(tasks);
      this.ready = true;
      return this;
    }

    /** 取某组动画的第 i 帧（Image），越界返回该组第 0 帧 */
    get(anim, i) {
      const arr = this.images[anim];
      if (!arr || !arr.length) return null;
      return arr[Math.max(0, Math.min(i, arr.length - 1))];
    }

    count(anim) { return this.counts[anim] || 0; }
  }

  /**
   * 动画状态机 —— 复刻桌面端 pet.cs 的行为：
   *   平时播 idle；鼠标按下播 press（松开回 idle）；点击播 click；拖动播 drag。
   *   每组动画播完停在最后一帧（不循环），idle 例外（循环）。
   */
  class Animator {
    constructor(frameset) {
      this.fs = frameset;
      this.anim = 'idle';
      this.frame = 0;
      this.acc = 0;          // 累积时间
      this.holdUntil = 0;    // 非 idle 动画播完后保持到什么时候
      this.pressed = false;
      this.dragging = false;
    }

    setFrameset(fs) {
      this.fs = fs;
      this.anim = 'idle';
      this.frame = 0;
      this.acc = 0;
    }

    press() { this.pressed = true; }
    release() { this.pressed = false; }
    startDrag() { this.dragging = true; }
    endDrag() { this.dragging = false; }

    /** 触发一次点击动画 */
    click(now) {
      this.anim = 'click';
      this.frame = 0;
      this.acc = 0;
      this.holdUntil = now + 1000;   // 播完后保持 1 秒再回 idle
    }

    /**
     * @param {number} dt  距上次 tick 的毫秒
     * @param {number} now 当前时间戳
     * @returns {string} 当前应显示的动画组名
     */
    tick(dt, now) {
      if (!this.fs || !this.fs.ready) return this.anim;

      // 优先级：拖动 > 按下 > 点击保持 > 待机
      const want = this.dragging ? 'drag'
        : this.pressed ? 'press'
        : (now < this.holdUntil ? 'click' : 'idle');

      if (want !== this.anim) {
        this.anim = want;
        this.frame = 0;
        this.acc = 0;
      }

      const n = this.fs.count(this.anim);
      if (n <= 0) return this.anim;

      this.acc += dt;
      const step = 1000 / this.fs.fps;
      while (this.acc >= step) {
        this.acc -= step;
        // idle 循环；其他组播完停在最后一帧（桌面端行为）
        this.frame = this.anim === 'idle' ? (this.frame + 1) % n : Math.min(this.frame + 1, n - 1);
      }
      return this.anim;
    }

    /** 当前应绘制的帧 */
    current() {
      if (!this.fs) return null;
      return this.fs.get(this.anim, this.frame);
    }
  }

  global.QPet = global.QPet || {};
  global.QPet.FrameSet = FrameSet;
  global.QPet.Animator = Animator;
  global.QPet.ANIM = ANIM;
})(window);
