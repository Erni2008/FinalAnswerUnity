const fs=require('fs'),path=require('path'),assert=require('assert/strict'),{spawn}=require('child_process'),{randomUUID}=require('crypto');
const root=path.resolve(__dirname,'../../Artifacts'),signal=path.join(root,'solo-ownership-room.json'),url='http://127.0.0.1:18096';
let state,ws,child;
const pause=ms=>new Promise(r=>setTimeout(r,ms));
async function until(fn,label){const end=Date.now()+35000;while(!fn()){assert(Date.now()<end,label);await pause(100);}}
function mode(value){const file=signal+'.control.json',tmp=file+'.tmp';fs.writeFileSync(tmp,JSON.stringify({id:randomUUID(),mode:value,manualPlayer:0,answers:'mixed',action:''}));fs.renameSync(tmp,file);}
function send(type,payload){ws.send(JSON.stringify({type,requestId:randomUUID(),payload}));}
function status(){try{return JSON.parse(fs.readFileSync(signal+'.status.json'));}catch{return null;}}
(async()=>{
 const response=await fetch(url+'/rooms',{method:'POST',body:new URLSearchParams({name:'Solo ownership test',maxPlayers:'8',gameMode:'standard',questionCount:'1',password:''})});assert(response.ok);const room=await response.json();
 ws=new WebSocket(url.replace('http:','ws:')+'/ws');ws.onmessage=e=>{const m=JSON.parse(e.data);if(m.payload?.revision!==undefined)state=m.payload;};
 await new Promise((resolve,reject)=>{ws.onopen=resolve;ws.onerror=reject;});send('AUTH',{roomCode:room.code,token:room.token});await until(()=>state?.lobbyId,'Host AUTH');
 mode('manual');fs.writeFileSync(signal,JSON.stringify({nonce:randomUUID(),command:'room',code:room.code,url,players:3}));const fd=fs.openSync(path.join(root,'solo-ownership-bots.log'),'w');child=spawn(process.execPath,[path.resolve(__dirname,'../manual-qa-phones.cjs'),signal],{env:{...process.env,FA_QA_HEADLESS:'1'},stdio:['ignore',fd,fd]});
 await until(()=>state?.players.length===3&&state.players.every(p=>p.connectionStatus==='ONLINE'),'Three controlled phones');
 send('UPDATE_IMPOSTOR_SETTINGS',{roomCode:room.code,sessionId:state.lobbyId,settings:{roundCount:1,discussionSeconds:5,votingSeconds:5,clueSeconds:15,wordCategory:'DEFAULT',soundEnabled:true,visualEffectsEnabled:true,reducedMotion:false}});
 await until(()=>state.impostorSettings.roundCount===1,'Settings ACK');send('START_IMPOSTOR',{roomCode:room.code,sessionId:state.lobbyId,participantIds:state.players.map(p=>p.id)});
 await until(()=>state.impostor?.phase==='ROUND_CLUE_INPUT','Real clue input');await pause(1400);assert.equal(state.impostor.clueReadyIds.length,0,'All-manual must never enter a clue');
 mode('assist');await until(()=>state.impostor.clueReadyIds.length===2,'Two bots act while developer owns first player');await pause(1200);
 assert.equal(status().players[0].lastAction,'');assert.equal(status().players[0].bot,false);assert.equal(state.impostor.clueReadyIds.length,2,'Manual seat must remain untouched');
 mode('auto');await until(()=>state.impostor.revealedClues.length===1,'All-auto resumes the human seat');assert.equal(Object.keys(state.impostor.revealedClues[0]).length,3);
 fs.writeFileSync(path.join(root,'solo-qa-takeover-result.json'),JSON.stringify({allManualSubmittedNothing:true,assistKeptHumanSeatUntouched:true,twoBotsSubmitted:true,allAutoResumedHumanSeat:true,realJavaValidation:true},null,2));console.log('LIVE_SOLO_OWNERSHIP_OK: manual -> assist -> auto, real server and phone DOM');send('LEAVE_ROOM',{roomCode:room.code});
})().catch(e=>{console.error(e.stack);process.exitCode=1;}).finally(()=>{ws?.close();child?.kill('SIGTERM');});
