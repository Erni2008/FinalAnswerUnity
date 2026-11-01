const fs=require('fs'),path=require('path');
process.env.PLAYWRIGHT_BROWSERS_PATH ||= path.join(__dirname,'.browsers');
const {chromium}=require('playwright');
const {decide,botEnabled}=require('./qa-bot-policy.cjs');
const questions=JSON.parse(fs.readFileSync(path.join(__dirname,'fixtures/questions.ru.json'),'utf8')).questions;
const signal=process.argv[2]||path.resolve(__dirname,'../Artifacts/manual-qa-room.json');
const pause=ms=>new Promise(r=>setTimeout(r,ms));
const characters=['skeleton','ghost','zombie','dracula','pumpkin','plague_doctor','voodoo','puppeteer'];
let stopping=false,browser,clients=[],info,config={mode:'assist',manualPlayer:0,answers:'mixed'},error='',controlId='',focusKey='',lastStatus=0;
const attempts=new Map();
function read(p){try{return JSON.parse(fs.readFileSync(p,'utf8'));}catch{return null;}}
function status(){
 const value={room:info?.code||'',mode:config.mode,manualPlayer:config.manualPlayer,error,players:clients.map((p,i)=>({index:i,online:!p.offline&&!p.page.isClosed()&&!!p.state?.selfId,bot:botEnabled(i,config),phase:p.state?.cursedBoxes?.phase||p.state?.impostor?.phase||p.state?.phase||'',submitted:!!p.state?.hasSubmittedAnswer,lastAction:p.lastAction||''}))};
 const tmp=signal+'.status.json.tmp';fs.writeFileSync(tmp,JSON.stringify(value));fs.renameSync(tmp,signal+'.status.json');
}
async function closeContexts(){for(const p of clients)await p.context.close().catch(()=>{});clients=[];attempts.clear();}
async function stop(){stopping=true;await browser?.close().catch(()=>{});}
process.on('SIGINT',()=>stop());process.on('SIGTERM',()=>stop());
async function instrument(page){await page.addInitScript(()=>{window.__qaState=null;const Original=window.WebSocket;window.WebSocket=class extends Original{constructor(...args){super(...args);this.addEventListener('message',e=>{try{const m=JSON.parse(e.data);if(m.payload?.revision!==undefined)window.__qaState=m.payload;}catch{}})}};});}
async function openRoom(next){
 await closeContexts();info=next;error='';focusKey='';
 if(next.command!=='room')return;
 if(!browser?.isConnected())browser=await chromium.launch({headless:process.env.FA_QA_HEADLESS==='1',args:['--disable-session-crashed-bubble']});
 const count=Math.max(2,Math.min(8,next.players||3));
 for(let i=0;i<count;i++){
  const context=await browser.newContext({viewport:{width:390,height:780}}),page=await context.newPage();
  const client={context,page,offline:false,state:null,lastAction:''};clients.push(client);
  await instrument(page);await page.goto(next.url);await page.locator('#room-code').fill(next.code);await page.locator('#nickname').fill('Тест '+(i+1));await page.locator('#character').selectOption(characters[i]);await page.locator('#join-button').click();
  await page.waitForFunction(()=>window.__qaState?.selfId,{timeout:20000});client.state=await page.evaluate(()=>window.__qaState);
  if(process.env.FA_QA_HEADLESS!=='1'){const session=await context.newCDPSession(page);const {windowId}=await session.send('Browser.getWindowForTarget');await session.send('Browser.setWindowBounds',{windowId,bounds:{left:25+i%4*360,top:60,width:400,height:880,windowState:'normal'}});}
 }
 console.log('SOLO_QA_CONTROLLERS_READY: '+next.code+' players='+count);
}
async function control(next){
 config={...config,...next};
 if(next.id&&next.id!==controlId){controlId=next.id;
  const client=clients[next.manualPlayer||0];
  if(client&&next.action==='connection'){
   client.offline=!client.offline;focusKey="";
   await client.page.goto(client.offline?'about:blank':info.url);
   if(!client.offline){await client.page.waitForFunction(()=>window.__qaState?.selfId,{timeout:10000});client.state=await client.page.evaluate(()=>window.__qaState);}
  }
 }
 const key=config.mode+':'+config.manualPlayer;
 if(key!==focusKey&&config.mode==='assist'&&clients[config.manualPlayer]&&!clients[config.manualPlayer].offline){focusKey=key;await clients[config.manualPlayer].page.bringToFront();}
}
async function act(client,action){
 if(action.kind==='unsupported'){error=action.message;return;}
 const last=attempts.get(action.key)||{at:0,count:0};if(Date.now()-last.at<1500)return;
 attempts.set(action.key,{at:Date.now(),count:last.count+1});const page=client.page,index=clients.indexOf(client);const allowed=()=>botEnabled(index,{...config,...(read(signal+'.control.json')||{})});const click=locator=>{if(!allowed())throw Error('QA_MANUAL_TAKEOVER');return locator.click({timeout:800});};if(!allowed())return;
 if(action.kind==='answer')await click(page.locator('#answers button').nth(action.index));
 if(action.kind==='hide'||action.kind==='fire'){await click(page.locator('#cursed-grid button').nth(action.box));await click(page.locator('#cursed-fire'));}
 if(action.kind==='clue'){await page.locator('#clue-input').fill(action.words[last.count%action.words.length],{timeout:800});await click(page.locator('#clue-submit'));}
 if(action.kind==='suspicion')await click(page.locator('#suspect-cards button[data-id="'+action.target+'"]'));
 if(action.kind==='vote'){await click(page.locator('#final-cards button[data-id="'+action.target+'"]'));await click(page.locator('#confirm-vote'));}
 client.lastAction=action.kind;
}
(async()=>{
 let nonce='';
 while(!stopping){
  const next=read(signal);
  if(next&&next.nonce!==nonce){nonce=next.nonce;await openRoom(next);}
  const settings=read(signal+'.control.json');if(settings)try{await control(settings);}catch(e){error='Контроллер: '+e.message.split('\n')[0];}
  for(let i=0;i<clients.length;i++){
   const c=clients[i];if(c.offline||c.page.isClosed())continue;
   try{c.state=await c.page.evaluate(()=>window.__qaState);const action=decide(c.state,i,config,questions);if(action)await act(c,action);}
   catch(e){if(!e.message.includes('Timeout')&&!e.message.includes('closed')&&e.message!=='QA_MANUAL_TAKEOVER')error=e.message.split('\n')[0];}
  }
  if(Date.now()-lastStatus>500){lastStatus=Date.now();status();}
  await pause(100);
 }
})().catch(e=>{console.error(e.message);process.exitCode=1;}).finally(()=>stop());
