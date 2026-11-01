const fs=require('fs'),path=require('path'),{spawn,spawnSync}=require('child_process');
const project=path.resolve(__dirname,'..'),artifacts=path.join(project,'Artifacts');fs.mkdirSync(artifacts,{recursive:true});
const host=process.argv.includes('--host');
let server,phones,app,cleaning=false;
const pause=ms=>new Promise(r=>setTimeout(r,ms));
async function healthy(){try{return (await fetch('http://127.0.0.1:18096/health',{signal:AbortSignal.timeout(1500)})).ok;}catch{return false;}}
function log(name){return fs.openSync(path.join(artifacts,name),'w');}
function run(file,args){const r=spawnSync(file,args,{stdio:'inherit'});if(r.status!==0)throw Error(file+' failed ('+r.status+')');}
async function cleanup(){if(cleaning)return;cleaning=true;if(app&&app.exitCode===null)app.kill('SIGTERM');if(phones&&phones.exitCode===null){phones.kill('SIGTERM');for(let i=0;i<30&&phones.exitCode===null;i++)await pause(100);}if(server)try{process.kill(-server.pid,'SIGTERM');}catch{}}
process.on('SIGINT',()=>cleanup().then(()=>process.exit(0)));process.on('SIGTERM',()=>cleanup().then(()=>process.exit(0)));
(async()=>{
 if(!host&&!fs.existsSync(path.join(__dirname,'node_modules/playwright')))run('npm',['ci','--prefix',__dirname,'--no-audit','--no-fund']);
 process.env.PLAYWRIGHT_BROWSERS_PATH=path.join(__dirname,'.browsers');
 if(!host){const {chromium}=require('playwright');if(!fs.existsSync(chromium.executablePath()))run(process.execPath,[path.join(__dirname,'install-browser.cjs')]);}
 if(!await healthy()){
  const java=path.resolve(project,'../FinalAnswer'),fd=log('manual-qa-server.log');
  server=spawn(path.join(java,'gradlew'),['-p',java,':server:lastCommandDev','-Plc.port=18096','--offline','--no-daemon','--console=plain'],{cwd:java,detached:true,stdio:['ignore',fd,fd]});
  let ready=false;for(let i=0;i<120;i++){if(await healthy()){ready=true;break;}if(server.exitCode!==null)break;await pause(500);}if(!ready)throw Error('Java server did not start. See Artifacts/manual-qa-server.log');
 }
 const signal=path.join(artifacts,'manual-qa-room.json');try{if(!host)fs.unlinkSync(signal);}catch(e){if(e.code!=='ENOENT')throw e;}
 if(!host){const fd=log('manual-qa-phones.log');phones=spawn(process.execPath,[path.join(__dirname,'manual-qa-phones.cjs'),signal],{env:process.env,stdio:['ignore',fd,fd]});}
 app=spawn('/usr/bin/open',['-W','-n','-a',path.join(project,'Builds/macOS/FinalAnswerUnity.app'),'--args',...(host?[]:['-fa-manual-qa','-fa-qa-signal',signal]),'-screen-fullscreen','0','-screen-width','1600','-screen-height','1000','-logFile',path.join(artifacts,host?'host-player.log':'manual-qa-player.log')],{stdio:'inherit'});
 await new Promise((resolve,reject)=>{app.once('error',reject);app.once('exit',code=>code===0?resolve():reject(Error('Unity launch failed ('+code+')')));});
})().catch(e=>{console.error(e.message);process.exitCode=1;}).finally(cleanup);
