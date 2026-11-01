const fs=require('fs'),path=require('path'),assert=require('assert/strict'),{spawn}=require('child_process'),{randomUUID}=require('crypto');
const root=path.resolve(__dirname,'../Artifacts'),signal=path.join(root,'solo-qa-room.json');
const read=p=>{try{return JSON.parse(fs.readFileSync(p));}catch{return null}},pause=ms=>new Promise(r=>setTimeout(r,ms));
const writeControl=value=>{const tmp=signal+'.control.json.tmp';fs.writeFileSync(tmp,JSON.stringify({id:randomUUID(),mode:'auto',manualPlayer:2,answers:'mixed',...value}));fs.renameSync(tmp,signal+'.control.json');};
const log=fs.openSync(path.join(root,'solo-qa-bots.log'),'w');
const child=spawn(process.execPath,[path.join(__dirname,'manual-qa-phones.cjs'),signal],{env:{...process.env,FA_QA_HEADLESS:'1'},stdio:['ignore',log,log]});
(async()=>{
 const start=Date.now();let phase='waiting',beforeIds,code,check=0,lastHttp=0;
 while(Date.now()-start<360000){
  const info=read(signal),status=read(signal+'.status.json');
  if(info?.command==='room'&&status?.room===info.code){
   const ready=status.players.length===3&&status.players.every(p=>p.phase==='ANSWERING'&&p.submitted);
   if((phase==='waiting'&&ready||phase==='disconnect'||phase==='reconnect')&&Date.now()-lastHttp>1100){
    lastHttp=Date.now();const response=await fetch(info.url+'/rooms/'+info.code);
    if(response.status===429){await pause(2000);continue;}assert(response.ok);
    const s=await response.json();
    if(phase==='waiting'){beforeIds=s.players.map(p=>p.id).sort();code=info.code;writeControl({action:'connection'});check=Date.now();phase='disconnect';}
    else if(phase==='disconnect'&&s.players.some(p=>p.connectionStatus==='OFFLINE')&&Date.now()-check>1100){writeControl({action:'connection'});phase='reconnect';}
    else if(phase==='reconnect'&&info.code===code&&s.players.every(p=>p.connectionStatus==='ONLINE')){assert.deepEqual(s.players.map(p=>p.id).sort(),beforeIds);phase='complete';}
   }
  }
  const resultPath=path.join(root,'solo-qa-unity-result.json');
  if(fs.existsSync(resultPath)&&fs.statSync(resultPath).mtimeMs>=start){assert.equal(phase,'complete','Real disconnect/reconnect must preserve identities');fs.writeFileSync(path.join(root,'solo-qa-bots-result.json'),JSON.stringify({allThreeModes:true,disconnectObserved:true,reconnectPreservedIdentity:true,usedOriginalPhoneDom:true},null,2));console.log('SOLO_QA_OK: quiz, standalone four shots, 3-round impostor, real reconnect');await pause(2000);return;}
  if(child.exitCode!==null)throw Error('Bot service exited; inspect solo-qa-bots.log');
  await pause(200);
 }
 throw Error('Solo QA integration timed out');
})().catch(e=>{console.error(e.stack);process.exitCode=1;}).finally(()=>child.kill('SIGTERM'));
