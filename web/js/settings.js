/* ==========================================================================
   settings.js —— 设置中心
   页面结构对齐桌面端：气泡 / 通用 / 角色 / 动画 / 语音 / 统计
   每项只写「用户需要知道的」，细节放 title 提示（与桌面端文案精简原则一致）。
   ========================================================================== */
(function (global) {
  'use strict';

  const { colorUtil } = global.QPet;
  const { darken, lighten, inkFor } = colorUtil;

  const PAGES = [
    { id: 'bubble', name: '气泡' },
    { id: 'char',   name: '角色' },
    { id: 'anim',   name: '动画' },
    { id: 'voice',  name: '语音' },
    { id: 'ui',     name: '通用' },
    { id: 'stats',  name: '统计' },
  ];

  const BUBBLE_COLORS = ['#8FA6D8', '#E8F1FB', '#FFD8E8', '#D8F3E0', '#FFE8C8', '#E8D8FF', '#F5E6D0', '#2A2A32'];
  const FONT_SIZES = [9, 10, 10.5, 11, 12, 14, 16, 18, 20, 24];
  const THEMES = ['#12C87A', '#005FB8', '#8B5CF6', '#EF4444', '#F59E0B', '#EC4899', '#0EA5E9', '#1A1A1E'];

  function el(tag, attrs, children) {
    const n = document.createElement(tag);
    if (attrs) for (const k in attrs) {
      if (k === 'class') n.className = attrs[k];
      else if (k === 'html') n.innerHTML = attrs[k];
      else if (k === 'text') n.textContent = attrs[k];
      else if (k.startsWith('on') && typeof attrs[k] === 'function') n.addEventListener(k.slice(2), attrs[k]);
      else if (attrs[k] !== null && attrs[k] !== undefined) n.setAttribute(k, attrs[k]);
    }
    if (children) (Array.isArray(children) ? children : [children]).forEach(c => {
      if (c) n.appendChild(typeof c === 'string' ? document.createTextNode(c) : c);
    });
    return n;
  }

  class SettingsPanel {
    constructor(pet, stats) {
      this.pet = pet;
      this.stats = stats;
      this.page = 'bubble';
      this.tabsEl = document.getElementById('tabs');
      this.bodyEl = document.getElementById('panel-body');
      this._buildTabs();
      this.pet.onConfigChange(() => this.render());
      this.render();
    }

    _buildTabs() {
      this.tabsEl.innerHTML = '';
      for (const p of PAGES) {
        const b = el('button', {
          class: 'tab' + (p.id === this.page ? ' on' : ''),
          text: p.name,
          onclick: () => { this.page = p.id; this._buildTabs(); this.render(); },
        });
        this.tabsEl.appendChild(b);
      }
    }

    render() {
      const b = this.bodyEl;
      b.innerHTML = '';
      const cfg = this.pet.cfg;
      switch (this.page) {
        case 'bubble': this._bubble(b, cfg); break;
        case 'char':   this._char(b, cfg); break;
        case 'anim':   this._anim(b, cfg); break;
        case 'voice':  this._voice(b, cfg); break;
        case 'ui':     this._ui(b, cfg); break;
        case 'stats':  this._stats(b, cfg); break;
      }
    }

    /* ---------------- 通用组件 ---------------- */
    _row(label, sub, ctl) {
      return el('div', { class: 'row' }, [
        el('div', { class: 'row-label' }, [
          el('b', { text: label }),
          sub ? el('span', { text: sub }) : null,
        ]),
        el('div', { class: 'row-ctl' }, ctl),
      ]);
    }

    _switch(on, onchange) {
      const s = el('div', { class: 'switch' + (on ? ' on' : ''), role: 'switch' });
      s.addEventListener('click', () => {
        const nv = !s.classList.contains('on');
        s.classList.toggle('on', nv);
        onchange(nv);
      });
      return s;
    }

    _slider(val, min, max, step, fmt, onchange) {
      const out = el('span', { class: 'slider-val', text: fmt(val) });
      const r = el('input', { type: 'range', class: 'slider', min, max, step, value: val });
      r.addEventListener('input', () => { out.textContent = fmt(+r.value); onchange(+r.value); });
      return [r, out];
    }

    _select(value, options, onchange) {
      const s = el('select', { class: 'input' });
      for (const o of options) {
        const op = el('option', { value: o.value ?? o, text: o.label ?? o });
        if ((o.value ?? o) === value) op.selected = true;
        s.appendChild(op);
      }
      s.addEventListener('change', () => onchange(s.value));
      return s;
    }

    /* ---------------- 气泡页 ---------------- */
    _bubble(b, cfg) {
      b.appendChild(el('h1', { class: 'page-title', text: '对话气泡' }));
      b.appendChild(el('p', { class: 'page-sub', text: '台词与音效' }));

      const card = el('div', { class: 'card' });
      card.appendChild(el('div', { class: 'card-head' }, [el('h3', { text: '外观' })]));

      card.appendChild(this._row('显示气泡', null, [this._switch(cfg.bubbleOn, v => this.pet.update({ bubbleOn: v }))]));
      card.appendChild(this._row('气泡高度', '像素', this._slider(cfg.bubbleH, 60, 220, 4, v => v + 'px', v => this.pet.update({ bubbleH: v }))));
      card.appendChild(this._row('样式', null, this._select(cfg.bubbleStyle, [
        { value: 'card', label: '柔和渐变' },
        { value: 'comic', label: '漫画描边' },
        { value: 'classic', label: '椭圆对话框' },
      ], v => this.pet.update({ bubbleStyle: v }))));

      // 色板
      const sw = el('div', { class: 'swatches' });
      for (const c of BUBBLE_COLORS) {
        const s = el('div', {
          class: 'swatch' + (cfg.bubbleColor.toLowerCase() === c.toLowerCase() ? ' on' : ''),
          style: 'background:' + c,
          title: c,
          onclick: () => {
            sw.querySelectorAll('.swatch').forEach(x => x.classList.remove('on'));
            s.classList.add('on');
            this.pet.update({ bubbleColor: c, bubbleInk: 'auto' });
          },
        });
        sw.appendChild(s);
      }
      card.appendChild(el('div', { class: 'row' }, [
        el('div', { class: 'row-label' }, [el('b', { text: '气泡颜色' })]),
        el('div', { class: 'row-ctl' }, [sw]),
      ]));
      b.appendChild(card);

      // 文字
      const card2 = el('div', { class: 'card' });
      card2.appendChild(el('div', { class: 'card-head' }, [el('h3', { text: '文字' })]));
      card2.appendChild(this._row('字号', 'pt', this._slider(cfg.bubbleSize, 9, 24, 0.5, v => v + 'pt', v => this.pet.update({ bubbleSize: v }))));
      card2.appendChild(this._row('字体', null, this._select(cfg.bubbleFont, [
        { value: 'YouYuan', label: '幼圆' },
        { value: 'Microsoft YaHei', label: '微软雅黑' },
        { value: 'SimSun', label: '宋体' },
        { value: 'SimHei', label: '黑体' },
        { value: 'KaiTi', label: '楷体' },
        { value: 'FangSong', label: '仿宋' },
      ], v => {
        this.pet.bubble.fontFamily = v === 'YouYuan' ? '"YouYuan","Microsoft YaHei",sans-serif' : `"${v}","Microsoft YaHei",sans-serif`;
        this.pet.bubble._skin = null;
        this.pet.update({ bubbleFont: v });
      })));
      b.appendChild(card2);

      // 预览
      const prev = el('div', { class: 'bubble-preview' });
      const skin = el('div', {
        class: 'bubble-skin',
        text: '这是一条示例台词，用来预览气泡的样子。',
        style: 'background:' + cfg.bubbleColor + ';color:' + inkFor(cfg.bubbleColor),
      });
      prev.appendChild(skin);
      const card3 = el('div', { class: 'card' });
      card3.appendChild(el('div', { class: 'card-head' }, [el('h3', { text: '预览' })]));
      card3.appendChild(prev);
      b.appendChild(card3);
    }

    /* ---------------- 角色页 ---------------- */
    _char(b, cfg) {
      b.appendChild(el('h1', { class: 'page-title', text: '角色与大小' }));
      b.appendChild(el('p', { class: 'page-sub', text: '选择角色并调整大小' }));

      const grid = el('div', { class: 'char-grid' });
      for (const c of this.pet.chars) {
        const item = el('div', {
          class: 'char-item' + (c.id === cfg.char ? ' on' : ''),
          onclick: async () => {
            grid.querySelectorAll('.char-item').forEach(x => x.classList.remove('on'));
            item.classList.add('on');
            await this.pet.setChar(c.id);
            this.stats.enterChar(c.id);
          },
        }, [
          el('img', { src: `assets/chars/${encodeURIComponent(c.id)}/portrait.webp`, alt: c.name, loading: 'lazy' }),
          el('span', { text: c.name }),
        ]);
        grid.appendChild(item);
      }
      const card = el('div', { class: 'card' });
      card.appendChild(el('div', { class: 'card-head' }, [el('h3', { text: '角色' }), el('span', { class: 'sub', text: this.pet.chars.length + ' 个' })]));
      card.appendChild(grid);
      b.appendChild(card);

      const card2 = el('div', { class: 'card' });
      card2.appendChild(el('div', { class: 'card-head' }, [el('h3', { text: '大小' })]));
      card2.appendChild(this._row('显示缩放', null, this._slider(cfg.scale, 50, 200, 5, v => v + '%', v => this.pet.update({ scale: v }))));
      card2.appendChild(el('div', { class: 'notice', text: '在舞台上拖拽可以移动位置。' }));
      b.appendChild(card2);

      // 导入角色包
      const card3 = el('div', { class: 'card' });
      card3.appendChild(el('div', { class: 'card-head' }, [el('h3', { text: '导入角色包' })]));
      const fileInput = el('input', { type: 'file', accept: '.zip', style: 'display:none' });
      fileInput.addEventListener('change', async () => {
        const f = fileInput.files && fileInput.files[0];
        if (!f) return;
        await this._importZip(f);
        fileInput.value = '';
      });
      card3.appendChild(fileInput);
      card3.appendChild(el('div', { style: 'display:flex;gap:8px;flex-wrap:wrap' }, [
        el('button', { class: 'btn', text: '选择 zip 包', onclick: () => fileInput.click() }),
      ]));
      card3.appendChild(el('div', { class: 'notice', style: 'margin-top:10px', text: '包内需含 char.json、frames/*.webp、voices/*.mp3。' }));
      b.appendChild(card3);
    }

    /** 导入 zip 角色包 */
    async _importZip(file) {
      const toast = global.QPet.toast;
      try {
        toast('正在解析 ' + file.name + '…');
        const buf = await file.arrayBuffer();
        const files = await global.QPet.CharStore.unzip(buf);
        if (!Object.keys(files).length) throw new Error('压缩包是空的');

        const base = file.name.replace(/\.zip$/i, '') || 'my-char';
        const id = 'user_' + base + '_' + Date.now().toString(36);
        const pack = await global.QPet.CharStore.buildPack(id, files);
        await global.QPet.CharStore.savePack(pack);

        // 加入内存角色表
        this.pet.chars.push({ id, name: pack.name, voiceCount: pack.voices.length, user: true });
        toast('已导入「' + pack.name + '」，共 ' + pack.frames.length + ' 帧 / ' + pack.voices.length + ' 语音');
        this.render();
      } catch (e) {
        toast('导入失败：' + (e && e.message || e));
      }
    }

    /* ---------------- 动画页 ---------------- */
    _anim(b, cfg) {
      b.appendChild(el('h1', { class: 'page-title', text: '动画' }));
      b.appendChild(el('p', { class: 'page-sub', text: '四组动画各自独立' }));

      const card = el('div', { class: 'card' });
      const rows = [
        ['idle', '待机'], ['press', '按下'], ['click', '点击'], ['drag', '拖动'],
      ];
      for (const [k, label] of rows) {
        card.appendChild(this._row(label, null, [
          this._switch(cfg.animOn[k], v => {
            const animOn = Object.assign({}, cfg.animOn); animOn[k] = v;
            this.pet.update({ animOn });
          }),
        ]));
      }
      b.appendChild(card);

      const card2 = el('div', { class: 'card' });
      card2.appendChild(el('div', { class: 'card-head' }, [el('h3', { text: '幅度与倍速' })]));
      for (const [k, label] of rows) {
        card2.appendChild(this._row(label + ' 倍速', null, this._slider(cfg.speed[k], 25, 200, 5, v => v + '%', v => {
          const speed = Object.assign({}, cfg.speed); speed[k] = v;
          this.pet.update({ speed });
        })));
      }
      card2.appendChild(el('div', { class: 'notice', text: '幅度需保存后生效，倍速即时生效。' }));
      b.appendChild(card2);
    }

    /* ---------------- 语音页 ---------------- */
    _voice(b, cfg) {
      b.appendChild(el('h1', { class: 'page-title', text: '语音' }));
      b.appendChild(el('p', { class: 'page-sub', text: '台词与音效' }));

      const card = el('div', { class: 'card' });
      card.appendChild(this._row('音量', null, this._slider(cfg.volume, 0, 100, 1, v => v + '%', v => this.pet.update({ volume: v }))));
      card.appendChild(this._row('点击时说话', null, [this._switch(cfg.clickVoice, v => this.pet.update({ clickVoice: v }))]));
      card.appendChild(this._row('说话方式', null, this._select(cfg.voiceMode, [
        { value: 'random', label: '随机抽一句' },
        { value: 'sequential', label: '按顺序挨个说' },
      ], v => this.pet.update({ voiceMode: v }))));
      b.appendChild(card);

      // 台词库
      const lines = this.pet.charData ? (this.pet.charData.voices || []) : [];
      const card2 = el('div', { class: 'card' });
      card2.appendChild(el('div', { class: 'card-head' }, [
        el('h3', { text: '台词库' }),
        el('span', { class: 'sub', text: lines.length + ' 条' }),
      ]));
      if (!lines.length) {
        card2.appendChild(el('div', { class: 'notice warn', text: '当前角色没有语音。' }));
      } else {
        const list = el('div', { class: 'line-list' });
        lines.forEach((l, i) => {
          const row = el('div', { class: 'line-row' }, [
            el('span', { class: 'txt', text: l.text, title: l.text }),
            el('span', { class: 't', text: '#' + (i + 1) }),
            el('button', {
              class: 'btn sm', text: '播放',
              onclick: async (e) => {
                e.stopPropagation();
                await this.pet.audio.play(l);
                this.pet.bubble.show(l.text, Date.now(), 5200);
              },
            }),
          ]);
          list.appendChild(row);
        });
        card2.appendChild(list);
        card2.appendChild(el('div', { style: 'margin-top:10px' }, [
          el('button', {
            class: 'btn', text: '随机来一句',
            onclick: async () => {
              const t = await this.pet.audio.playNext();
              if (t) this.pet.bubble.show(t, Date.now(), 5200);
            },
          }),
        ]));
      }
      b.appendChild(card2);
    }

    /* ---------------- 通用页 ---------------- */
    _ui(b, cfg) {
      b.appendChild(el('h1', { class: 'page-title', text: '通用设置' }));
      b.appendChild(el('p', { class: 'page-sub', text: '外观与行为' }));

      const card = el('div', { class: 'card' });
      card.appendChild(el('div', { class: 'card-head' }, [el('h3', { text: '主题色' })]));
      const sw = el('div', { class: 'swatches' });
      for (const t of THEMES) {
        const s = el('div', {
          class: 'swatch' + (cfg.theme.toLowerCase() === t.toLowerCase() ? ' on' : ''),
          style: 'background:' + t,
          title: t,
          onclick: () => {
            sw.querySelectorAll('.swatch').forEach(x => x.classList.remove('on'));
            s.classList.add('on');
            this.pet.update({ theme: t });
          },
        });
        sw.appendChild(s);
      }
      card.appendChild(sw);
      b.appendChild(card);

      const card2 = el('div', { class: 'card' });
      card2.appendChild(el('div', { class: 'card-head' }, [el('h3', { text: '数据' })]));
      card2.appendChild(el('div', { class: 'notice', text: '所有数据仅保存在本机浏览器，不联网、不上传。' }));
      card2.appendChild(el('div', { style: 'margin-top:10px' }, [
        el('button', {
          class: 'btn', text: '重置位置',
          onclick: () => { this.pet.resetPos(); toast('已重置位置'); },
        }),
        el('button', {
          class: 'btn', style: 'margin-left:8px', text: '恢复默认设置',
          onclick: () => {
            localStorage.removeItem('qpet.config.v1');
            toast('已恢复默认，刷新后生效');
          },
        }),
      ]));
      b.appendChild(card2);
    }

    /* ---------------- 统计页 ---------------- */
    _stats(b, cfg) {
      b.appendChild(el('h1', { class: 'page-title', text: '使用统计' }));
      b.appendChild(el('p', { class: 'page-sub', text: '仅统计本机' }));

      const s = this.stats.summary();
      const fmtMs = ms => {
        const m = Math.floor(ms / 60000);
        if (m < 60) return m + ' 分';
        const h = Math.floor(m / 60);
        return h + ' 小时 ' + (m % 60) + ' 分';
      };

      const card = el('div', { class: 'card' });
      card.appendChild(el('div', { class: 'card-head' }, [el('h3', { text: '概览' })]));
      const grid = el('div', { class: 'stat-grid' });
      grid.appendChild(el('div', { class: 'stat-box' }, [el('b', { text: fmtMs(s.totalMs) }), el('span', { text: '总陪伴时长' })]));
      grid.appendChild(el('div', { class: 'stat-box' }, [el('b', { text: String(s.interactions) }), el('span', { text: '互动次数' })]));
      grid.appendChild(el('div', { class: 'stat-box' }, [el('b', { text: String(s.lines) }), el('span', { text: '台词触发' })]));
      grid.appendChild(el('div', { class: 'stat-box' }, [el('b', { text: String(s.sessions) }), el('span', { text: '打开次数' })]));
      card.appendChild(grid);
      b.appendChild(card);

      if (s.chars.length) {
        const card2 = el('div', { class: 'card' });
        card2.appendChild(el('div', { class: 'card-head' }, [el('h3', { text: '角色陪伴时长' })]));
        const bl = el('div', { class: 'bar-list' });
        const max = Math.max(...s.chars.map(c => c.ms)) || 1;
        for (const c of s.chars) {
          const nm = (this.pet.chars.find(x => x.id === c.id) || {}).name || c.id;
          bl.appendChild(el('div', { class: 'bar-item' }, [
            el('span', { class: 'n', text: nm }),
            el('span', { class: 't' }, [el('i', { style: 'width:' + Math.round(c.ms / max * 100) + '%' })]),
            el('span', { class: 'v', text: fmtMs(c.ms) }),
          ]));
        }
        card2.appendChild(bl);
        b.appendChild(card2);
      }

      const card3 = el('div', { class: 'card' });
      card3.appendChild(el('div', { class: 'notice', text: '网页版无法读取系统级按键与软件使用数据，因此统计范围为页面内行为。' }));
      card3.appendChild(el('div', { style: 'margin-top:10px' }, [
        el('button', { class: 'btn', text: '清空统计', onclick: () => { this.stats.clear(); this.render(); toast('已清空'); } }),
      ]));
      b.appendChild(card3);
    }
  }

  global.QPet = global.QPet || {};
  global.QPet.SettingsPanel = SettingsPanel;
  global.QPet.el = el;
  global.QPet.toast = function (msg) {
    const t = document.getElementById('toast');
    if (!t) return;
    t.textContent = msg;
    t.classList.add('show');
    clearTimeout(global.__toastT);
    global.__toastT = setTimeout(() => t.classList.remove('show'), 2200);
  };
})(window);
