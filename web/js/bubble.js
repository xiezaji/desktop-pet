/* ==========================================================================
   bubble.js —— 对话气泡
   移植自桌面端 pet.cs 的 BuildBubbleSkin / DrawBubbleInto：
     · 四种皮肤：card（渐变圆角）/ classic（椭圆漫画）/ comic（白底粗边）/ custom（九宫格外框）
     · 出现 170ms 缓动放大 + 淡入，消失前 240ms 淡出
     · 文字装不下时可**手动滚动**（滚轮/拖动），配一条极细滚动条
   ========================================================================== */
(function (global) {
  'use strict';

  /* ---------- 颜色工具 ---------- */
  function hexToRgb(hex) {
    const h = (hex || '#8FA6D8').replace('#', '');
    const s = h.length === 3 ? h.split('').map(c => c + c).join('') : h;
    return [parseInt(s.slice(0, 2), 16), parseInt(s.slice(2, 4), 16), parseInt(s.slice(4, 6), 16)];
  }
  function rgbToHex(r, g, b) {
    const c = v => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, '0');
    return '#' + c(r) + c(g) + c(b);
  }
  function darken(hex, amt) {
    const [r, g, b] = hexToRgb(hex);
    return rgbToHex(r * (1 - amt), g * (1 - amt), b * (1 - amt));
  }
  function lighten(hex, amt) {
    const [r, g, b] = hexToRgb(hex);
    return rgbToHex(r + (255 - r) * amt, g + (255 - g) * amt, b + (255 - b) * amt);
  }
  /** 按底色亮度自动选深/浅文字（复刻桌面端 InkFor） */
  function inkFor(hex) {
    const [r, g, b] = hexToRgb(hex);
    const lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
    return lum > 0.62 ? '#1A1A1A' : '#FFFFFF';
  }

  /* ---------- 路径：圆角矩形 + 尾巴（复刻 BubblePath） ---------- */
  function bubblePath(ctx, l, t, r, b, rad, tailL, tailR, tipX, tipY) {
    const d = rad * 2;
    ctx.beginPath();
    ctx.arc(l, t, d / 2, Math.PI, Math.PI * 1.5);          // 左上
    ctx.arc(r - d, t, d / 2, Math.PI * 1.5, Math.PI * 2);  // 右上
    ctx.arc(r - d, b - d, d / 2, 0, Math.PI * 0.5);        // 右下
    ctx.lineTo(tailR, b);
    ctx.lineTo(tipX, tipY);
    ctx.lineTo(tailL, b);
    ctx.arc(l, b - d, d / 2, Math.PI * 0.5, Math.PI);      // 左下
    ctx.closePath();
  }

  class Bubble {
    constructor(canvas) {
      this.canvas = canvas;
      this.ctx = canvas ? canvas.getContext('2d') : null;
      this.text = '';
      this.shownAt = 0;
      this.until = 0;
      this.scroll = 0;
      this.scrollMax = 0;
      this.hover = false;

      // 样式（可被设置页改）
      this.style = 'card';
      this.accent = '#8FA6D8';
      this.ink = null;
      this.fontFamily = '"Microsoft YaHei", "PingFang SC", sans-serif';
      this.fontSize = 13;         // pt → px 换算在 setFontSize 里做
      this.lineHeight = 1.42;
      this.needScroll = false;
      this.textRect = { x: 0, y: 0, w: 0, h: 0 };
      this.measureCanvas = document.createElement('canvas');
      this.measureCtx = this.measureCanvas.getContext('2d');
      this._skin = null;
      this._skinKey = '';
    }

    setFontSize(pt) {
      // 桌面端字号单位是 pt（1pt = 1.333px @96dpi）
      this.fontSize = pt * 4 / 3;
      this._skin = null;
    }

    show(text, now, duration) {
      this.text = text || '';
      this.shownAt = now;
      this.until = now + (duration || 5200);
      this.scroll = 0;
      this._skin = null;
    }

    hide() { this.until = 0; }

    get visible() { return this.text !== '' && Date.now() < this.until; }

    /** 滚轮/拖动滚动（仅在文字装不下时生效） */
    scrollBy(delta) {
      if (!this.needScroll) return;
      this.scroll = Math.max(0, Math.min(this.scrollMax, this.scroll + delta));
    }

    /** 画到 ctx（ctx 已被平移到气泡区原点） */
    draw(ctx, w, h, now) {
      if (!this.visible) { this.needScroll = false; return; }

      const appear = Math.min(1, (now - this.shownAt) / 170);
      const left = this.until - now;
      const fadeOut = left < 240 ? Math.max(0, left / 240) : 1;
      const alpha = Math.min(1, appear * 1.6) * fadeOut;
      if (alpha <= 0.02) { this.needScroll = false; return; }
      const ease = 1 - (1 - appear) * (1 - appear);
      const scale = 0.94 + 0.06 * ease;

      const skin = this._buildSkin(w, h);
      if (!skin) return;

      // 以尾巴尖为轴做弹出缩放（复刻桌面端）
      const cx = w / 2, axisY = h;
      ctx.save();
      ctx.translate(cx, axisY);
      ctx.scale(scale, scale);
      ctx.translate(-cx, -axisY);
      ctx.globalAlpha = alpha;
      ctx.drawImage(skin, 0, 0, w, h);
      ctx.restore();

      // 文字
      const ink = this.ink || inkFor(this.accent);
      const font = this.fontSize + 'px ' + this.fontFamily;
      this.measureCtx.font = font;
      const innerW = this.textRect.w;
      const lines = this._wrap(this.measureCtx, this.text, innerW);
      const lh = this.fontSize * this.lineHeight;
      const total = lines.length * lh + 6;

      if (total > this.textRect.h) {
        this.needScroll = true;
        this.scrollMax = Math.max(0, total - this.textRect.h + 8);
        this.scroll = Math.max(0, Math.min(this.scroll, this.scrollMax));
      } else {
        this.needScroll = false;
        this.scroll = 0;
      }

      ctx.save();
      ctx.globalAlpha = alpha;
      ctx.font = font;
      ctx.textBaseline = 'top';
      ctx.fillStyle = ink;
      if (this.needScroll) {
        // 裁剪到气泡内再画，实现滚动
        ctx.save();
        ctx.beginPath();
        ctx.rect(this.textRect.x, this.textRect.y - 1, innerW, this.textRect.h + 2);
        ctx.clip();
        const y0 = this.textRect.y - this.scroll;
        for (let i = 0; i < lines.length; i++) {
          ctx.fillText(lines[i], this.textRect.x, y0 + i * lh);
        }
        ctx.restore();

        // 右侧极细滚动条：让用户知道「还有内容、现在读到哪」
        const trackX = this.textRect.x + innerW - 2.5;
        const trackY = this.textRect.y;
        const trackH = this.textRect.h;
        ctx.fillStyle = 'rgba(0,0,0,.10)';
        ctx.fillRect(trackX, trackY, 2, trackH);
        const knobH = Math.max(18, trackH * (this.textRect.h / total));
        const knobY = trackY + (trackH - knobH) * (this.scroll / (this.scrollMax || 1));
        ctx.fillStyle = 'rgba(0,0,0,.30)';
        ctx.fillRect(trackX, knobY, 2, knobH);
      } else {
        for (let i = 0; i < lines.length; i++) {
          ctx.fillText(lines[i], this.textRect.x, this.textRect.y + i * lh);
        }
      }
      ctx.restore();
    }

    /** 按宽度折行（中文按字断行，英文按空格） */
    _wrap(mctx, text, maxW) {
      if (!text) return [];
      const out = [];
      let line = '';
      const isCJK = ch => /[一-鿿　-〿＀-￯]/.test(ch);
      for (let i = 0; i < text.length; i++) {
        const ch = text[i];
        if (ch === '\n') { out.push(line); line = ''; continue; }
        const test = line + ch;
        if (mctx.measureText(test).width > maxW && line) {
          if (!isCJK(ch) && /\S/.test(ch)) {
            // 西文单词：回退到最后一个空格
            const sp = line.lastIndexOf(' ');
            if (sp > 0) {
              out.push(line.slice(0, sp));
              line = line.slice(sp + 1) + ch;
              continue;
            }
          }
          out.push(line);
          line = ch;
        } else {
          line = test;
        }
      }
      if (line) out.push(line);
      return out;
    }

    /** 生成气泡皮肤（缓存，样式变化才重画） */
    _buildSkin(w, h) {
      const key = w + 'x' + h + '|' + this.style + '|' + this.accent + '|' + this.fontSize;
      if (this._skin && this._skinKey === key) return this._skin;

      const sw = Math.max(1.4, w * 4.6 / 1026);
      const pad = Math.max(9, w * 0.052);
      const tailH = Math.min(15, h * 0.15);
      const bodyL = sw + 1.5, bodyR = w - sw - 1.5;
      const bodyT = sw + 1.5, bodyB = h - tailH - sw - 1.5;
      const radius = Math.max(8, Math.min(15, (bodyB - bodyT) * 0.32));
      const cx = (bodyL + bodyR) / 2;
      const tailTipY = bodyB + tailH;
      const tailL = cx - 15, tailR = cx + 7, tipX = cx - 5;

      this.ink = inkFor(this.accent);
      this.textRect = {
        x: bodyL + pad, y: bodyT + pad * 0.72,
        w: Math.max(20, (bodyR - bodyL) - pad * 2),
        h: Math.max(16, (bodyB - bodyT) - pad * 1.45),
      };

      const cv = document.createElement('canvas');
      cv.width = Math.ceil(w); cv.height = Math.ceil(h);
      const g = cv.getContext('2d');
      g.clearRect(0, 0, w, h);

      if (this.style === 'classic') {
        this._drawClassic(g, w, h, sw, bodyL, bodyT, bodyR, bodyB, cx);
      } else if (this.style === 'comic') {
        this._drawComic(g, sw, bodyL, bodyT, bodyR, bodyB, radius, tailL, tailR, tipX, tailTipY);
      } else {
        this._drawCard(g, sw, bodyL, bodyT, bodyR, bodyB, radius, tailL, tailR, tipX, tailTipY);
      }

      this._skin = cv;
      this._skinKey = key;
      return cv;
    }

    _drawCard(g, sw, bodyL, bodyT, bodyR, bodyB, radius, tailL, tailR, tipX, tailTipY) {
      // 柔和投影（两层）
      g.save();
      g.translate(0, 2.5);
      g.fillStyle = 'rgba(42,58,102,.09)';
      bubblePath(g, bodyL, bodyT, bodyR, bodyB, radius, tailL, tailR, tipX, tailTipY);
      g.fill();
      g.translate(0, 2.5);
      g.fillStyle = 'rgba(42,58,102,.06)';
      bubblePath(g, bodyL, bodyT, bodyR, bodyB, radius, tailL, tailR, tipX, tailTipY);
      g.fill();
      g.restore();

      // 白 → 淡蓝渐变
      const lg = g.createLinearGradient(0, bodyT, 0, tailTipY);
      lg.addColorStop(0, '#FFFFFF');
      lg.addColorStop(1, lighten(this.accent, 0.78));
      g.fillStyle = lg;
      bubblePath(g, bodyL, bodyT, bodyR, bodyB, radius, tailL, tailR, tipX, tailTipY);
      g.fill();
      g.strokeStyle = this.accent;
      g.globalAlpha = 0.84;
      g.lineWidth = sw;
      bubblePath(g, bodyL, bodyT, bodyR, bodyB, radius, tailL, tailR, tipX, tailTipY);
      g.stroke();
      g.globalAlpha = 1;

      // 顶部高光
      const inset = radius + 4;
      if (bodyR - bodyL > inset * 2) {
        g.strokeStyle = 'rgba(255,255,255,.5)';
        g.lineWidth = Math.max(1, sw * 0.7);
        g.beginPath();
        g.moveTo(bodyL + inset, bodyT + sw * 1.6);
        g.lineTo(bodyR - inset, bodyT + sw * 1.6);
        g.stroke();
      }
    }

    _drawComic(g, sw, bodyL, bodyT, bodyR, bodyB, radius, tailL, tailR, tipX, tailTipY) {
      // 偏移投影
      g.save();
      g.translate(5, 5);
      g.fillStyle = 'rgba(0,0,0,.16)';
      bubblePath(g, bodyL, bodyT, bodyR, bodyB, radius, tailL, tailR, tipX, tailTipY);
      g.fill();
      g.restore();

      // 白底 + 粗描边
      g.fillStyle = '#FFFFFF';
      bubblePath(g, bodyL, bodyT, bodyR, bodyB, radius, tailL, tailR, tipX, tailTipY);
      g.fill();
      g.strokeStyle = darken(this.accent, 0.55);
      g.lineWidth = Math.max(2.4, sw * 1.7);
      g.lineJoin = 'round';
      bubblePath(g, bodyL, bodyT, bodyR, bodyB, radius, tailL, tailR, tipX, tailTipY);
      g.stroke();
      this.ink = darken(this.accent, 0.78);
    }

    _drawClassic(g, w, h, sw, bodyL, bodyT, bodyR, bodyB, cx) {
      const rx = (bodyR - bodyL) / 2, ry = (bodyB - bodyT) / 2;
      const ecx = cx, ecy = (bodyT + bodyB) / 2;
      const csw = Math.max(2.4, sw * 2.1);
      const r1 = w * 0.048, x1 = cx - rx * 0.74, y1 = h - r1 * 1.25;
      const r2 = w * 0.031, x2 = cx - rx * 0.33, y2 = h - r2 * 0.85;
      const stroke = darken(this.accent, 0.30);
      const shd = darken(this.accent, 0.45);

      // 椭圆影
      g.fillStyle = 'rgba(0,0,0,.09)';
      g.beginPath(); g.ellipse(ecx, ecy + 3, rx, ry, 0, 0, Math.PI * 2); g.fill();
      g.beginPath(); g.ellipse(x1, y1 + 2, r1, r1 * 0.7, 0, 0, Math.PI * 2); g.fill();
      g.beginPath(); g.ellipse(x2, y2 + 2, r2, r2 * 0.7, 0, 0, Math.PI * 2); g.fill();

      // 白底椭圆 + 描边
      g.fillStyle = '#FFFFFF';
      g.strokeStyle = stroke;
      g.lineWidth = csw;
      const ell = (cx2, cy2, rx2, ry2) => {
        g.beginPath(); g.ellipse(cx2, cy2, rx2, ry2, 0, 0, Math.PI * 2);
        g.fill(); g.stroke();
      };
      ell(ecx, ecy, rx, ry);
      ell(x1, y1, r1, r1 * 0.7);
      ell(x2, y2, r2, r2 * 0.7);

      this.ink = darken(this.accent, 0.55);
      this.textRect = {
        x: ecx - rx * 0.80, y: ecy - ry * 0.74,
        w: rx * 1.60, h: ry * 1.48,
      };
    }
  }

  global.QPet = global.QPet || {};
  global.QPet.Bubble = Bubble;
  global.QPet.colorUtil = { hexToRgb, rgbToHex, darken, lighten, inkFor };
})(window);
