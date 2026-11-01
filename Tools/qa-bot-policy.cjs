// Developer-only phone decisions. Java still validates every action and owns all scores.
const clueWords=['КАМЕНЬ','ВЕТЕР','ОБЛАКО','ЛИСТ','ЗВУК','ТЕНЬ','ЛУЧ','ПЕСОК','ВОЛНА','ТРАВА','СНЕГ','СВЕТ','ДОЖДЬ','БЕРЕГ','УТРО','ВЕЧЕР','ЛЕС','ХОЛМ','ДОРОГА','ЗВЕЗДА'];
function botEnabled(index,c){return c.mode==='auto'||c.mode==='assist'&&index!==c.manualPlayer;}
function decide(s,index,c,questions){
 if(!s||!botEnabled(index,c))return null;
 const cursed=s.cursedBoxes;
 if(cursed){
  const base=cursed.sessionId+':'+s.selfId;
  if(cursed.canHide&&!cursed.hidingConfirmed){const boxes=cursed.boxes.filter(b=>b.state==='AVAILABLE');return boxes.length?{kind:'hide',key:base+':hide',box:boxes[(5+index)%boxes.length].id}:null;}
  if(cursed.canAct&&!cursed.targetConfirmed&&['SELECTING','TARGET_SELECTION'].includes(cursed.phase)){const box=cursed.boxes.find(b=>b.state==='AVAILABLE');return box?{kind:'fire',key:base+':fire:'+cursed.turnId+':'+cursed.shotsRemaining,box:box.id}:null;}
  return null;
 }
 const m=s.impostor;
 if(m){
  if(!m.activeParticipant)return null;
  const base=m.sessionId+':'+s.selfId+':'+m.round;
  if(m.phase==='ROUND_CLUE_INPUT'&&!m.clueSubmitted){
   const forbidden=new Set([m.secretWord,...(m.revealedClues||[]).map(r=>r[s.selfId])].filter(Boolean).map(x=>x.toUpperCase()));
   const words=clueWords.filter(w=>!forbidden.has(w));
   return {kind:'clue',key:base+':clue',words:words.slice(index).concat(words.slice(0,index))};
  }
  const target=m.participants.find(p=>p.id!==s.selfId);
  if(target&&m.phase==='PRELIMINARY_SUSPICION'&&m.eligible&&!m.hasSubmitted)return {kind:'suspicion',key:base+':suspect',target:target.id};
  if(target&&m.phase==='FINAL_VOTING'&&!m.finalVoteSubmitted)return {kind:'vote',key:m.sessionId+':'+s.selfId+':vote',target:target.id};
  return null;
 }
 if(s.phase==='ANSWERING'&&!s.hasSubmittedAnswer&&s.question){
  const q=questions.find(q=>q.id===s.question.id);
  if(!q)return {kind:'unsupported',key:s.gameId+':'+s.question.id,message:'Нет QA-ключа вопроса '+s.question.id};
  const correct=c.answers==='correct'||c.answers!=='wrong'&&index===0;
  return {kind:'answer',key:s.gameId+':'+s.question.id+':'+s.selfId,index:correct?q.correctAnswerIndex:(q.correctAnswerIndex+1)%s.question.answers.length};
 }
 return null;
}
module.exports={botEnabled,decide};
