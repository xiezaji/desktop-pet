/* ==========================================================================
   app.js —— 启动入口
   ========================================================================== */
(function (global) {
  'use strict';

  const { QPet, Stats, SettingsPanel, toast } = global.QPet;

  async function boot() {
    const canvas = document.getElementById('pet-canvas');
    const loader = document.getElementById('loader');
    const stats = new Stats();
    const pet = new QPet();

    try {
      await pet.init(canvas, loader);
    } catch (e) {
      console.error(e);
      const t = loader.querySelector('.loader-title');
      const s = loader.querySelector('.loader-sub');
      if (t) t.textContent = '加载失败';
      if (s) s.textContent = String(e && e.message || e);
      return;
    }

    stats.enterChar(pet.cfg.char);
    stats.start();

    // 顶栏信息
    const pc = document.getElementById('pill-char');
    const pv = document.getElementById('pill-voice');
    const pf = document.getElementById('pill-fps');
    if (pc) pc.querySelector('b').textContent = (pet.charData && pet.charData.name) || '—';
    if (pv) pv.querySelector('b').textContent = ((pet.charData && pet.charData.voices || []).length) + ' 条';

    // 帧率统计
    let frames = 0, last = performance.now();
    (function fpsLoop() {
      frames++;
      const now = performance.now();
      if (now - last >= 1000) {
        if (pf) pf.querySelector('b').textContent = Math.round(frames * 1000 / (now - last)) + ' fps';
        frames = 0; last = now;
      }
      requestAnimationFrame(fpsLoop);
    })();

    // 面板
    const panel = document.getElementById('panel');
    const fab = document.getElementById('btn-open-panel');
    new SettingsPanel(pet, stats);

    document.getElementById('btn-close-panel').addEventListener('click', () => {
      panel.classList.add('collapsed');
      fab.classList.add('show');
      // 面板收起后舞台变宽，角色重新居中
      setTimeout(() => { pet._resizeCanvas(); pet._centerPos(); pet._applyPos(); }, 320);
    });
    fab.addEventListener('click', () => {
      panel.classList.remove('collapsed');
      fab.classList.remove('show');
      setTimeout(() => { pet._resizeCanvas(); pet._clampPos(); pet._applyPos(); }, 320);
    });
    document.getElementById('btn-reset-pos').addEventListener('click', () => {
      pet.resetPos();
      toast('已重置位置');
    });

    // 右键菜单（对应桌面端的「打开设置 / 退出桌宠」）
    canvas.addEventListener('contextmenu', (e) => {
      e.preventDefault();
      const items = [
        ['随机说一句', () => pet._autoLine(true)],
        ['打开设置', () => { panel.classList.remove('collapsed'); fab.classList.remove('show'); }],
        ['重置位置', () => { pet.resetPos(); toast('已重置位置'); }],
      ];
      showMenu(e.clientX, e.clientY, items);
    });

    // 台词触发计入统计
    const origShow = pet.bubble.show.bind(pet.bubble);
    pet.bubble.show = (t, n, d) => { stats.interact('line'); return origShow(t, n, d); };
    canvas.addEventListener('click', () => stats.interact('click'));

    global.__pet = pet;

  }

  function showMenu(x, y, items) {
    document.querySelectorAll('.ctx-menu').forEach(m => m.remove());
    const m = document.createElement('div');
    m.className = 'ctx-menu';
    Object.assign(m.style, {
      position: 'fixed', left: x + 'px', top: y + 'px',
      background: '#fff', border: '1px solid #EDEDF0', borderRadius: '8px',
      boxShadow: '0 8px 28px rgba(0,0,0,.14)', padding: '4px', zIndex: 200, minWidth: '150px',
    });
    for (const [label, fn] of items) {
      const b = document.createElement('button');
      b.textContent = label;
      Object.assign(b.style, {
        display: 'block', width: '100%', textAlign: 'left', padding: '7px 12px',
        border: '0', background: 'transparent', borderRadius: '4px', cursor: 'pointer',
        font: 'inherit', fontSize: '13px', color: '#1A1A1E',
      });
      b.addEventListener('mouseenter', () => b.style.background = '#F3F3F5');
      b.addEventListener('mouseleave', () => b.style.background = 'transparent');
      b.addEventListener('click', () => { m.remove(); fn(); });
      m.appendChild(b);
    }
    document.body.appendChild(m);
    const close = () => { m.remove(); document.removeEventListener('click', close); };
    setTimeout(() => document.addEventListener('click', close), 0);
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
  else boot();
})(window);
