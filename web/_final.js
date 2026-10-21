const http=require('http');
const fs=require('fs');
const get=p=>new Promise((s,j)=>http.get({host:'127.0.0.1',port:9223,path:p},r=>{let b='';r.on('data',d=>b+=d);r.on('end',()=>s(JSON.parse(b)))}).on('error',j));
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
(async()=>{
  const list=await get('/json/list');
  const t=list.find(x=>x.type==='page'&&x.url&&x.url.startsWith('http://127.0.0.1:8778'));
  if(!t){console.log('no page');process.exit(1);}
  const ws=new WebSocket(t.webSocketDebuggerUrl);
  await new Promise(r=>ws.onopen=r);
  let id=0; const w=new Map(); const errs=[];
  ws.onmessage=e=>{const m=JSON.parse(e.data);
    if(m.id&&w.has(m.id)){const{res}=w.get(m.id);w.delete(m.id);res(m.result);}
    else if(m.method==='Runtime.exceptionThrown') errs.push(m.params.exceptionDetails.text);
    else if(m.method==='Runtime.consoleAPICalled'&&m.params.type==='error') errs.push(m.params.args.map(a=>a.value||a.description).join(' '));
  };
  const send=(m,p)=>new Promise(res=>{const i=++id;w.set(i,{res});ws.send(JSON.stringify({id:i,method:m,params:p||{}}));});
  const ev=async x=>{const r=await send('Runtime.evaluate',{expression:x,returnByValue:true,awaitPromise:true});
    if(r.exceptionDetails) return 'EXC: '+r.exceptionDetails.text; return r.result.value;};
  await send('Page.enable');
  await send('Page.reload',{});
  await sleep(9000);

  console.log('1 init:', await ev("(()=>{const p=window.__pet;return p.cfg.char+' frames='+(p.frameset&&p.frameset.ready)+' voice='+p.audio.lines.length+' chars='+p.chars.length})()"));

  await ev("[...document.querySelectorAll('.tab')].find(b=>b.textContent==='语音').click()");
  await sleep(500);
  await ev("[...document.querySelectorAll('button')].find(b=>b.textContent==='随机来一句').click()");
  await sleep(1500);
  console.log('2 bubble:', await ev("(()=>{const p=window.__pet;return 'vis='+p.bubble.visible+' scroll='+p.bubble.needScroll+' text='+(p.bubble.text||'').slice(0,12)})()"));

  await ev("[...document.querySelectorAll('.tab')].find(b=>b.textContent==='角色').click()");
  await sleep(500);
  const idx = await ev("(()=>{const items=[...document.querySelectorAll('.char-item')];return items.findIndex(e=>e.textContent.includes('小维'))})()");
  console.log('3 xiaowei index:', idx);
  if (idx >= 0) {
    await ev("document.querySelectorAll('.char-item')[" + idx + "].click()");
    await sleep(6000);
    console.log('4 switched:', await ev("(()=>{const p=window.__pet;return p.charData.name+' voice='+p.audio.lines.length+' ready='+p.frameset.ready})()"));
    await ev("[...document.querySelectorAll('.tab')].find(b=>b.textContent==='语音').click()");
    await sleep(500);
    await ev("[...document.querySelectorAll('button')].find(b=>b.textContent==='随机来一句').click()");
    await sleep(1500);
    console.log('5 bubble2:', await ev("(()=>{const p=window.__pet;return 'vis='+p.bubble.visible+' text='+(p.bubble.text||'').slice(0,18)})()"));
  }

  await send('Page.captureScreenshot',{format:'png'}).then(r=>fs.writeFileSync('final.png',Buffer.from(r.data,'base64')));
  console.log('ERRORS:', JSON.stringify(errs));
  process.exit(0);
})();
