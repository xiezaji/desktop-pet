/* ==========================================================================
   core.js —— 主控
   复刻桌面端 pet.cs 的整体行为：
     · 双层画布（顶部气泡区 + 下方角色帧）
     · 鼠标按下/松开/点击/拖动 → 四种动画
     · 定时自动冒泡（随机一句台词）
     · 位置持久化（localStorage）
   ========================================================================== */
(function (global) {
  'use strict';

  const { FrameSet, Animator, Bubble, VoiceEngine } = global.QPet;
  const { inkFor } = global.QPet.colorUtil;

  const STORE_KEY = 'qpet.config.v1';
  const POS_KEY = 'qpet.pos.v1';

  const DEFAULT_CFG = {
    char: null,               // 当前角色 id
    bubbleOn: true,
    bubbleH: 110,
    bubbleEveryMs: 300000,
    bubbleFont: 'YouYuan',
    bubbleSize: 10.5,         // pt
    bubbleStyle: 'card',
    bubbleColor: '#8FA6D8',
    bubbleInk: 'auto',
    volume: 100,
    clickVoice: false,
    voiceMode: 'random',
    charH: 400,
    scale: 100,
    amp: { idle: 100, press: 100, click: 100, drag: 100 },
    animOn: { idle: true, press: true, click: true, drag: true },
    speed: { idle: 100, press: 100, click: 100, drag: 100 },
    theme: '#12C87A',
  };

  class QPet {
    constructor() {
      this.cfg = this._loadCfg();
      this.canvas = null;
      this.ctx = null;
      this.bubble = new Bubble(null);   // 气泡皮肤自建离屏画布，不需要外部 ctx
      this.audio = new VoiceEngine();
      this.frameset = null;
      this.animator = new Animator(null);
      this.chars = [];              // 角色索引
      this.charData = null;         // 当前角色的 char.json
      this.charBase = '';
      this.dpr = global.devicePixelRatio || 1;
      this.lastTick = 0;
      this.nextAutoBubble = 0;
      this.hoverCanvas = false;
      this.dragging = false;
      this.dragStart = null;
      this.pos = { x: 0, y: 0 };
      this._raf = null;
      this._onConfigChange = null;
    }

    /* ---------------- 配置持久化 ---------------- */
    _loadCfg() {
      try {
        const raw = localStorage.getItem(STORE_KEY);
        if (raw) return Object.assign({}, DEFAULT_CFG, JSON.parse(raw));
      } catch (e) { /* 读不到就用默认 */ }
      return Object.assign({}, DEFAULT_CFG);
    }
    saveCfg() {
      try { localStorage.setItem(STORE_KEY, JSON.stringify(this.cfg)); } catch (e) { /* 忽略 */ }
    }
    onConfigChange(fn) { this._onConfigChange = fn; }
    _emitConfig() { if (this._onConfigChange) this._onConfigChange(this.cfg); }

    /* ---------------- 初始化 ---------------- */
    async init(canvas, loaderEl) {
      this.canvas = canvas;
      this.ctx = canvas.getContext('2d');

      // 角色索引
      const idx = await fetch('assets/chars/index.json').then(r => r.json()).catch(() => ({ chars: [] }));
      this.chars = (idx.chars || []).map(c => Object.assign({ builtin: true }, c));
      if (!this.cfg.char && this.chars.length) {
        this.cfg.char = this.chars.find(c => c.voiceCount > 0)?.id || this.chars[0].id;
      }

      // 加载当前角色
      const target = this.cfg.char || (this.chars[0] && this.chars[0].id);
      if (!target) {
        if (loaderEl) loaderEl.querySelector('.loader-title').textContent = '没有可用角色';
        return;
      }
      await this.loadChar(target, loaderEl);

      this._bindEvents();
      this._applyTheme();

      // 定位要等布局完成：此时舞台 getBoundingClientRect 才拿得到真实尺寸，
      // 否则 _clampPos 会因为 box=0 直接跳过，角色就停在旧坐标上。
      requestAnimationFrame(() => {
        this._resizeCanvas();
        this._restorePos();
      });

      this.lastTick = performance.now();
      this.nextAutoBubble = Date.now() + 8000;
      this._loop();

      // 自动冒泡：按设定的间隔随机冒一句（复刻桌面端 bubbleEveryMs）
      this._autoTimer = setInterval(() => {
        const every = this.cfg.bubbleEveryMs || 300000;
        if (Date.now() < this.nextAutoBubble) return;
        this.nextAutoBubble = Date.now() + every;
        if (this.cfg.bubbleOn) this._autoLine(false);
      }, 5000);

      // 预加载下一角色（切换时能秒开）
      const nextC = this.chars.find(c => c.id !== target);
      if (nextC) this._prefetchChar(nextC.id);
    }

    async loadChar(id, loaderEl) {
      const base = 'assets/chars/' + encodeURIComponent(id);
      this.charBase = base;

      const setProgress = (d, t) => {
        if (!loaderEl) return;
        const bar = loaderEl.querySelector('.bar i');
        const tip = loaderEl.querySelector('.loader-tip');
        if (bar) bar.style.width = Math.round(d / t * 100) + '%';
        if (tip) tip.textContent = '加载素材 ' + d + '/' + t;
      };

      const meta = await fetch(base + '/char.json').then(r => r.json());
      this.charData = meta;
      this.cfg.char = id;
      this.saveCfg();

      const fs = new FrameSet(meta);
      await fs.load(base, meta.frames, setProgress);
      this.frameset = fs;
      this.animator.setFrameset(fs);

      // 语音
      this.audio.setLines(meta.voices || []);
      this.audio.setMode(this.cfg.voiceMode);
      this.audio.setVolume(this.cfg.volume / 100);
      this.audio.preload(base).catch(() => {});

      // 气泡配置
      this.bubble.style = this.cfg.bubbleStyle;
      this.bubble.accent = this.cfg.bubbleColor;
      this.bubble.setFontSize(this.cfg.bubbleSize);
      if (this.cfg.bubbleInk && this.cfg.bubbleInk !== 'auto') {
        this.bubble.ink = this.cfg.bubbleInk;
      } else {
        this.bubble.ink = inkFor(this.cfg.bubbleColor);
      }

      this._resizeCanvas();
      this._emitConfig();

      if (loaderEl) {
        loaderEl.classList.add('hide');
        setTimeout(() => loaderEl.remove(), 400);
      }
    }

    async _prefetchChar(id) {
      try {
        const base = 'assets/chars/' + encodeURIComponent(id);
        const meta = await fetch(base + '/char.json').then(r => r.json());
        const fs = new FrameSet(meta);
        await fs.load(base, meta.frames, null);
        this._prefetched = this._prefetched || {};
        this._prefetched[id] = { fs, meta };
      } catch (e) { /* 预加载失败无所谓 */ }
    }

    /* ---------------- 画布尺寸 ---------------- */
    _resizeCanvas() {
      if (!this.canvas || !this.frameset) return;
      const w = this.frameset.w, h = this.frameset.h;
      const bubbleH = this.cfg.bubbleOn ? this.cfg.bubbleH : 0;

      // 用户缩放 × 舞台自适应：画布不能超出舞台，否则角色会被裁掉
      const box = this._stageBox();
      const userScale = (this.cfg.scale || 100) / 100;
      let scale = userScale;
      if (box.w && box.h) {
        const fit = Math.min(box.w / w, box.h / (h + bubbleH));
        // 自适应系数不能突破用户设定（用户想要更大就允许超出，靠拖动看全）
        scale = Math.min(userScale, Math.max(0.3, fit));
      }

      const cssW = Math.round(w * scale);
      const cssH = Math.round((h + bubbleH) * scale);
      this.canvas.style.width = cssW + 'px';
      this.canvas.style.height = cssH + 'px';
      this.canvas.width = Math.round(cssW * this.dpr);
      this.canvas.height = Math.round(cssH * this.dpr);
      // 画布内部分辨率按「原始像素 × dpr」算，缩放只影响显示尺寸，
      // 这样 100% 时最清晰，缩小也不会糊
      this.ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
      this._bubbleH = bubbleH;
      this._scale = scale;
      this._userScale = userScale;
    }

    /* ---------------- 事件 ---------------- */
    _bindEvents() {
      const cv = this.canvas;

      cv.addEventListener('mousedown', (e) => {
        if (e.button !== 0) return;
        this.audio.ensureContext();
        this.animator.press();
        this.dragging = true;
        this.dragStart = { x: e.clientX, y: e.clientY, ox: this.pos.x, oy: this.pos.y };
        cv.classList.add('dragging');
        e.preventDefault();
      });

      global.addEventListener('mousemove', (e) => {
        if (!this.dragging || !this.dragStart) return;
        const dx = e.clientX - this.dragStart.x;
        const dy = e.clientY - this.dragStart.y;
        this.pos.x = this.dragStart.ox + dx;
        this.pos.y = this.dragStart.oy + dy;
        this._clampPos();
        this._applyPos();
      });

      global.addEventListener('mouseup', () => {
        if (!this.dragging) return;
        this.dragging = false;
        this.dragStart = null;
        cv.classList.remove('dragging');
        this.animator.release();
        this.animator.endDrag();
        this._savePos();
      });

      // 点击（拖动距离小于阈值才算点击）
      cv.addEventListener('click', (e) => {
        const moved = this.dragStart ? 0 : 0;
        this.animator.click(Date.now());
        if (this.cfg.clickVoice) {
          this.audio.playNext().then(txt => {
            if (txt) this.bubble.show(txt, Date.now(), 5200);
          });
        } else {
          this._autoLine(true);
        }
      });

      // 悬停在气泡上时滚轮 = 手动滚动气泡文字
      cv.addEventListener('wheel', (e) => {
        if (!this.bubble.needScroll) return;
        e.preventDefault();
        this.bubble.scrollBy(e.deltaY > 0 ? 30 : -30);
      }, { passive: false });

      // 拖动时播 drag 动画
      cv.addEventListener('mousemove', () => {
        if (this.dragging) this.animator.startDrag();
      });

      // 窗口尺寸变化 / 面板折叠后重新定位，避免角色被面板遮住
      let rt = null;
      const relayout = () => {
        clearTimeout(rt);
        rt = setTimeout(() => {
          this._resizeCanvas();
          this._clampPos();
          this._applyPos();
        }, 120);
      };
      global.addEventListener('resize', relayout);
      global.addEventListener('orientationchange', relayout);

      // 首次交互解锁音频
      const unlock = () => { this.audio.ensureContext(); };
      global.addEventListener('pointerdown', unlock, { once: true });
      global.addEventListener('keydown', unlock, { once: true });
    }

    /* ---------------- 台词 ---------------- */
    async _autoLine(force) {
      if (!this.cfg.bubbleOn) return;
      const txt = await this.audio.playNext();
      if (txt) {
        this.bubble.show(txt, Date.now(), 5200);
      } else {
        // 没有语音台词时给个兜底
        this.bubble.show('……', Date.now(), 2600);
      }
    }

    /* ---------------- 主循环 ---------------- */
    _loop() {
      const now = performance.now();
      const dt = Math.min(100, now - this.lastTick);
      this.lastTick = now;

      this.animator.tick(dt, Date.now());
      this._draw();
      this._raf = requestAnimationFrame(() => this._loop());
    }

    _draw() {
      const ctx = this.ctx;
      if (!ctx || !this.frameset) return;
      const w = this.frameset.w, h = this.frameset.h;
      const bubbleH = this._bubbleH || 0;
      const scale = this._scale || 1;

      ctx.save();
      ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
      ctx.clearRect(0, 0, this.canvas.width / this.dpr, this.canvas.height / this.dpr);
      ctx.scale(scale, scale);

      // 上层：气泡区
      if (bubbleH > 0) {
        this.bubble.draw(ctx, w, bubbleH, Date.now());
      }

      // 下层：角色帧
      const img = this.animator.current();
      if (img && img.complete && img.naturalWidth) {
        ctx.drawImage(img, 0, bubbleH, w, h);
      }
      ctx.restore();
    }

    /* ---------------- 位置 ---------------- */
    _applyPos() {
      if (!this.canvas) return;
      this.canvas.style.position = 'absolute';
      this.canvas.style.left = Math.round(this.pos.x) + 'px';
      this.canvas.style.top = Math.round(this.pos.y) + 'px';
    }

    /** 舞台可用区域（画布自身尺寸从布局里拿，避免 absolute 元素 offset 为 0） */
    _stageBox() {
      const wrap = this.canvas.parentElement;
      if (!wrap) return { w: 0, h: 0 };
      const r = wrap.getBoundingClientRect();
      return { w: r.width, h: r.height };
    }

    /** 居中并夹回可见范围 */
    _centerPos() {
      const box = this._stageBox();
      const cw = this.canvas.offsetWidth || this.canvas.width;
      const ch = this.canvas.offsetHeight || this.canvas.height;
      if (!box.w || !box.h) return;
      this.pos.x = Math.round((box.w - cw) / 2);
      this.pos.y = Math.round((box.h - ch) / 2);
      this._clampPos();
    }

    /** 保证角色不会拖出舞台（留 24px 边距，至少露出一角） */
    _clampPos() {
      const box = this._stageBox();
      const cw = this.canvas.offsetWidth || this.canvas.width;
      const ch = this.canvas.offsetHeight || this.canvas.height;
      if (!box.w || !box.h) return;
      const m = 24;
      // 画布比舞台大时（缩放 > 100%），至少要露出一角：下限取 max(0, ...)，
      // 不能用 m-ch，否则画布比舞台高时会被推到负坐标外。
      const minX = cw >= box.w ? 0 : Math.max(0, m - cw);
      const minY = ch >= box.h ? 0 : Math.max(0, m - ch);
      this.pos.x = Math.max(minX, Math.min(box.w - m, this.pos.x));
      this.pos.y = Math.max(minY, Math.min(box.h - m, this.pos.y));
    }

    _restorePos() {
      let saved = null;
      try {
        const raw = localStorage.getItem(POS_KEY);
        if (raw) saved = JSON.parse(raw);
      } catch (e) { /* 忽略 */ }
      if (saved && typeof saved.x === 'number' && typeof saved.y === 'number') {
        this.pos = saved;
        this._clampPos();
        // 窗口尺寸变了（换了分辨率/缩放）导致保存的位置完全跑出舞台 → 重新居中
        const box = this._stageBox();
        const cw = this.canvas.offsetWidth || this.canvas.width;
        const ch = this.canvas.offsetHeight || this.canvas.height;
        if (box.w && box.h && (this.pos.x + cw < 40 || this.pos.x > box.w - 40
            || this.pos.y + ch < 40 || this.pos.y > box.h - 40)) {
          this._centerPos();
        }
      } else {
        this._centerPos();
      }
      this._applyPos();
    }
    _savePos() {
      try { localStorage.setItem(POS_KEY, JSON.stringify(this.pos)); } catch (e) { /* 忽略 */ }
    }

    /* ---------------- 主题 ---------------- */
    _applyTheme() {
      const t = this.cfg.theme || '#12C87A';
      const r = document.documentElement.style;
      r.setProperty('--accent', t);
      // 派生深浅色
      const [rr, gg, bb] = global.QPet.colorUtil.hexToRgb(t);
      r.setProperty('--accent-hover', global.QPet.colorUtil.rgbToHex(rr * 0.94, gg * 0.94, bb * 0.94));
      r.setProperty('--accent-ink', global.QPet.colorUtil.rgbToHex(rr * 0.48, gg * 0.48, bb * 0.48));
      r.setProperty('--accent-bg', global.QPet.colorUtil.rgbToHex(rr * 0.92 + 255 * 0.08, gg * 0.92 + 255 * 0.08, bb * 0.92 + 255 * 0.08));
      r.setProperty('--accent-line', global.QPet.colorUtil.rgbToHex(rr * 0.74 + 255 * 0.26, gg * 0.74 + 255 * 0.26, bb * 0.74 + 255 * 0.26));
    }

    /* ---------------- 对外接口 ---------------- */
    async setChar(id) {
      if (id === this.cfg.char) return;
      const pre = this._prefetched && this._prefetched[id];
      if (pre) {
        this.charData = pre.meta;
        this.charBase = 'assets/chars/' + encodeURIComponent(id);
        this.frameset = pre.fs;
        this.animator.setFrameset(pre.fs);
        this.audio.setLines(pre.meta.voices || []);
        this.audio.setMode(this.cfg.voiceMode);
        this.audio.setVolume(this.cfg.volume / 100);
        this.cfg.char = id;
        this.saveCfg();
        this._resizeCanvas();
        this.nextAutoBubble = Date.now() + (this.cfg.bubbleEveryMs || 300000);
        this._emitConfig();
        return;
      }
      await this.loadChar(id, null);
      this.nextAutoBubble = Date.now() + (this.cfg.bubbleEveryMs || 300000);
    }

    update(patch) {
      Object.assign(this.cfg, patch);
      this.saveCfg();
      if (patch.volume !== undefined) this.audio.setVolume(patch.volume / 100);
      if (patch.voiceMode !== undefined) this.audio.setMode(patch.voiceMode);
      if (patch.bubbleStyle !== undefined) { this.bubble.style = patch.bubbleStyle; this.bubble._skin = null; }
      if (patch.bubbleColor !== undefined) {
        this.bubble.accent = patch.bubbleColor;
        this.bubble.ink = (patch.bubbleInk && patch.bubbleInk !== 'auto')
          ? patch.bubbleInk : inkFor(patch.bubbleColor);
        this.bubble._skin = null;
      }
      if (patch.bubbleSize !== undefined) this.bubble.setFontSize(patch.bubbleSize);
      if (patch.theme !== undefined) this._applyTheme();
      if (patch.scale !== undefined || patch.bubbleOn !== undefined || patch.bubbleH !== undefined) this._resizeCanvas();
      this._emitConfig();
    }

    resetPos() {
      this.pos = { x: 0, y: 0 };
      this._restorePos();
      this._savePos();
    }
  }

  global.QPet = global.QPet || {};
  global.QPet.QPet = QPet;
  global.QPet.DEFAULT_CFG = DEFAULT_CFG;
})(window);
