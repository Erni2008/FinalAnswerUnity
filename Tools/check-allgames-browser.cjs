const fs=require('fs'),path=require('path'),assert=require('assert');
process.env.PLAYWRIGHT_BROWSERS_PATH ||= path.join(__dirname,'.browsers');
const {chromium}=require('playwright');
const artifacts=path.resolve(__dirname,'../Artifacts'), pause=ms=>new Promise(r=>setTimeout(r,ms));
(async()=>{
 const browser=await chromium.launch({headless:true});const report={unmodifiedControllers:true,stages:[]};
 try {
 for(const stage of ['quiz','impostor']){
  let info;const start=Date.now();while(!info){try{const v=JSON.parse(fs.readFileSync(path.join(artifacts,'allgames-room.json')));if(v.stage===stage)info=v;}catch{}assert(Date.now()-start<150000,'Unity room timeout '+stage);await pause(250);}
  const contexts=[],pages=[];
  for(let i=0;i<3;i++){
   const context=await browser.newContext({viewport:{width:390,height:844}});contexts.push(context);const page=await context.newPage();
   await page.addInitScript(()=>{window.__qaState=null;const Original=window.WebSocket;window.WebSocket=class extends Original{constructor(...args){super(...args);this.addEventListener('message',e=>{try{const m=JSON.parse(e.data);if(m.payload?.revision!==undefined)window.__qaState=m.payload;}catch{}})}};});
   await page.goto(info.url);await page.locator('#room-code').fill(info.code);await page.locator('#nickname').fill('Unity QA '+(i+1));await page.locator('#character').selectOption(['skeleton','ghost','zombie'][i]);await page.locator('#join-button').click();await page.waitForFunction(()=>window.__qaState?.selfId);pages.push(page);
  }
  const actions=new Set(),phases=new Set();let complete=false;const deadline=Date.now()+160000;
  while(!complete&&Date.now()<deadline){
   for(let i=0;i<pages.length;i++){
    const page=pages[i],s=await page.evaluate(()=>window.__qaState);if(!s)continue;
    if(stage==='quiz'){
     if(s.phase==='ANSWERING'&&!s.hasSubmittedAnswer){const key='answer'+i+':'+s.question.id;if(!actions.has(key)){await page.locator('#answers button').nth(i===0?1:0).click();actions.add(key);}}
     const c=s.cursedBoxes;
     if(c){phases.add('CURSED_'+c.phase);
      if(c.canHide&&!c.hidingConfirmed){const key='hide'+i;if(!actions.has(key)){await page.locator('#cursed-grid button').nth(5+i).click();await page.locator('#cursed-fire').click();actions.add(key);}}
      if(c.canAct&&!c.targetConfirmed){const key='fire'+i+':'+c.turnId+':'+c.shotsRemaining;if(!actions.has(key)){const id=c.boxes.find(x=>x.state==='AVAILABLE').id;await page.locator('#cursed-grid button').nth(id).click();await page.locator('#cursed-fire').click();actions.add(key);}}
     }
     if(s.phase==='GAME_FINISHED'&&!c)complete=true;
    }else{
     const m=s.impostor;if(!m)continue;phases.add(m.phase);
     if(m.phase==='ROUND_CLUE_INPUT'&&!m.clueSubmitted){const key='clue'+i+':'+m.round;if(!actions.has(key)){await page.locator('#clue-input').fill(['КАМЕНЬ','НЕБО','ВЕТЕР'][i]);await page.locator('#clue-submit').click();actions.add(key);}}
     if(m.phase==='PRELIMINARY_SUSPICION'&&!m.hasSubmitted&&m.eligible){const key='suspicion'+i+':'+m.round;if(!actions.has(key)){await page.locator('#suspect-cards button:not([disabled])').first().click();actions.add(key);}}
     if(m.phase==='FINAL_VOTING'&&!m.finalVoteSubmitted){const key='vote'+i;if(!actions.has(key)){await page.locator('#final-cards button:not([disabled])').first().click();await page.locator('#confirm-vote').click();actions.add(key);}}
     if(m.phase==='RESULT')complete=true;
    }
   }
   await pause(100);
  }
  assert(complete,'Did not complete '+stage+'; phases '+[...phases]);
  for(let i=0;i<pages.length;i++)await pages[i].screenshot({path:path.join(artifacts,`allgames-${stage}-phone-${i+1}.png`)});
  report.stages.push({stage,phones:3,actions:[...actions],phases:[...phases],completed:true});fs.writeFileSync(path.join(artifacts,'allgames-browser-result.json'),JSON.stringify(report,null,2));
  for(const context of contexts)await context.close();
 }
 console.log('ALL_GAMES_BROWSER_OK: real phone UI, quiz -> cursed -> quiz, full impostor match');
 }finally{await browser.close();}
})().catch(e=>{console.error(e.stack);process.exit(1)});
