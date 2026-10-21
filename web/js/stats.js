/* ==========================================================================
   stats.js —— 使用统计
   桌面端靠 Win32 全局键盘钩子 + 窗口枚举，能统计「系统级按键 / 软件使用」。
   浏览器拿不到这些系统数据，所以网页版统计的是**页面内的真实行为**：
     · 停留时长（分角色）
     · 互动次数（点击/拖拽/滚动气泡）
     · 台词触发次数
   数据存 localStorage，纯本地不上传（与桌面端「数据仅保存在本地」一致）。
   ========================================================================== */
(function (global) {
  'use strict';

  const KEY = 'qpet.stats.v1';

  class Stats {
    constructor() {
      this.data = this._load();
      this.sessionStart = Date.now();
      this._tick = null;
    }

    _load() {
      try {
        const raw = localStorage.getItem(KEY);
        if (raw) return Object.assign({ chars: {}, interactions: 0, lines: 0, sessions: 0 }, JSON.parse(raw));
      } catch (e) { /* 忽略 */ }
      return { chars: {}, interactions: 0, lines: 0, sessions: 0 };
    }

    save() {
      try { localStorage.setItem(KEY, JSON.stringify(this.data)); } catch (e) { /* 忽略 */ }
    }

    /** 进入某个角色 */
    enterChar(id) {
      const c = this.data.chars[id] || (this.data.chars[id] = { ms: 0, clicks: 0, lines: 0 });
      this._cur = { id, since: Date.now() };
      this.data.sessions++;
      this.save();
    }

    /** 记录一次互动 */
    interact(kind) {
      this.data.interactions++;
      if (this._cur && this._cur.id) {
        const c = this.data.chars[this._cur.id];
        if (c) c[kind === 'line' ? 'lines' : 'clicks']++;
      }
      this.save();
    }

    /** 累计时长（每次调用结算一次） */
    flush() {
      if (this._cur) {
        const dt = Date.now() - this._cur.since;
        const c = this.data.chars[this._cur.id];
        if (c) c.ms += dt;
        this._cur.since = Date.now();
        this.save();
      }
    }

    start() {
      this._tick = setInterval(() => this.flush(), 5000);
      global.addEventListener('beforeunload', () => { this.flush(); this.save(); });
    }

    /** 汇总视图 */
    summary() {
      this.flush();
      const chars = Object.entries(this.data.chars)
        .map(([id, v]) => ({ id, ...v }))
        .sort((a, b) => b.ms - a.ms);
      const totalMs = chars.reduce((s, c) => s + c.ms, 0);
      return {
        totalMs,
        interactions: this.data.interactions,
        lines: this.data.lines,
        sessions: this.data.sessions,
        chars,
      };
    }

    clear() {
      this.data = { chars: {}, interactions: 0, lines: 0, sessions: 0 };
      this._cur = null;
      this.save();
    }
  }

  global.QPet = global.QPet || {};
  global.QPet.Stats = Stats;
})(window);
