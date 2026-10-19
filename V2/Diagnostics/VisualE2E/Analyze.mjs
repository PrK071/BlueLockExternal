// JavaScript | Diagnostics/VisualE2E/Analyze.mjs | Node.js | Independent time-aligned ground-truth evaluation; never imported by the controller.
import fs from 'node:fs/promises';
import path from 'node:path';
const mean=a=>a.length?a.reduce((x,y)=>x+y,0)/a.length:null;
const quantile=(a,p)=>a.length?[...a].sort((x,y)=>x-y)[Math.floor((a.length-1)*p)]:null;
const distance=(a,b,c,d)=>Math.hypot(a-c,b-d);
async function jsonl(file){try{return (await fs.readFile(file,'utf8')).split(/\r?\n/).filter(Boolean).map(JSON.parse);}catch(e){if(e.code==='ENOENT')return [];throw e;}}
function nearest(rows,t){if(!rows.length)return null;let lo=0,hi=rows.length;while(lo<hi){let m=(lo+hi)>>1;if(rows[m].received<t)lo=m+1;else hi=m;}return [rows[lo],rows[lo-1]].filter(Boolean).sort((a,b)=>Math.abs(a.received-t)-Math.abs(b.received-t))[0];}
export async function analyze(root,subdir='campaign50'){
 const dir=path.join(root,subdir),names=(await fs.readdir(dir)).filter(n=>n.endsWith('-mcp.json')).sort();
 const trials=[],allBallErrors=[],allActorErrors=[],scores=[],ages=[],visionTimes=[];
 for(const name of names){
  const evidence=JSON.parse(await fs.readFile(path.join(dir,name),'utf8'));const c=evidence.case,run=c.run;
  const frames=await jsonl(path.join(dir,run+'-frames.jsonl')),allTruth=await jsonl(path.join(dir,run+'-truth.jsonl'));
  const seenClocks=new Set();const truth=allTruth.filter(q=>{if(q.truth.run!==run||seenClocks.has(q.truth.clock))return false;seenClocks.add(q.truth.clock);return true;});
  const result=evidence.result||JSON.parse(await fs.readFile(path.join(dir,run+'-result.json'),'utf8'));
  const ballErrors=[],actorErrors=[];let missingStart=null,maxMissingSeconds=0,recoveries=0,missingFrames=0,stableStart=null,stableArrival=false;
  const truthDistances=truth.map(q=>distance(q.truth.px,q.truth.pz,q.truth.bx,q.truth.bz));
  for(let i=0;i<truth.length;i++){if(truthDistances[i]<=1.2){if(stableStart===null)stableStart=truth[i].truth.clock;if(truth[i].truth.clock-stableStart>=.4)stableArrival=true;}else stableStart=null;}
  for(const frame of frames){
   ages.push(frame.age*1000);visionTimes.push(frame.visionMs);
   if(!frame.ball){missingFrames++;if(missingStart===null)missingStart=frame.t;maxMissingSeconds=Math.max(maxMissingSeconds,frame.t-missingStart);}else if(missingStart!==null){recoveries++;maxMissingSeconds=Math.max(maxMissingSeconds,frame.t-missingStart);missingStart=null;}
   const match=nearest(truth,frame.t);if(!match||Math.abs(match.received-frame.t)>.15)continue;
   for(const detection of frame.detections){const ball=detection.type==='BALL';const actor=detection.type==='LOCAL_PLAYER';if(!ball&&!actor)continue;const error=distance(detection.x,detection.z,ball?match.truth.bx:match.truth.px,ball?match.truth.bz:match.truth.pz);(ball?ballErrors:actorErrors).push(error);(ball?allBallErrors:allActorErrors).push(error);scores.push({type:detection.type,score:detection.score,correct:error<=.5,error});}
  }
  const last=frames.at(-1);let declaration=last?nearest(truth,last.t+last.age):null;
  if(declaration&&Math.abs(declaration.received-(last.t+last.age))>.15)declaration=null;
  const declarationDistance=declaration?distance(declaration.truth.px,declaration.truth.pz,declaration.truth.bx,declaration.truth.bz):null;
  const finalDistance=evidence.truth?.distance??truthDistances.at(-1)??null;
  const complete=result.status==='visual-reached'&&declarationDistance!==null&&declarationDistance<=1.2&&result.heldAfter===0;
  trials.push({run,kind:c.kind,width:c.width,yaw:c.yaw,truthSamples:truth.length,rejectedWrongRun:allTruth.length-truth.length,initialDistance:truthDistances[0]??null,minimumDistance:truthDistances.length?Math.min(...truthDistances):null,finalDistance,declarationDistance,arrived:truthDistances.some(d=>d<=1.2),stableArrival,complete,falseVisualSuccess:result.status==='visual-reached'&&declarationDistance!==null&&declarationDistance>1.2,status:result.status,frames:frames.length,inputEvents:result.events,heldAfter:result.heldAfter,seconds:result.seconds,suspended:result.suspended,ballCoverage:frames.length?1-missingFrames/frames.length:null,calibrationCoverage:frames.length?frames.filter(f=>f.calibrated).length/frames.length:null,maxMissingSeconds,recoveries,ballMae:mean(ballErrors),ballRmse:mean(ballErrors)===null?null:Math.sqrt(mean(ballErrors.map(e=>e*e))),ballP95:quantile(ballErrors,.95),ballPrecisionWithinHalfStud:ballErrors.length?ballErrors.filter(e=>e<=.5).length/ballErrors.length:null,actorMae:mean(actorErrors),actorP95:quantile(actorErrors,.95),meanVisionMs:result.meanVisionMs,meanCaptureMs:result.meanCaptureMs,cpuSeconds:result.cpuSeconds,workingSet:result.workingSet,privateBytes:result.privateBytes});
 }
 const bins=[];for(const type of ['BALL','LOCAL_PLAYER'])for(const score of [...new Set(scores.filter(v=>v.type===type).map(v=>v.score))]){const r=scores.filter(v=>v.type===type&&v.score===score);bins.push({type,score,n:r.length,empiricalPrecisionWithinHalfStud:r.filter(v=>v.correct).length/r.length,mae:mean(r.map(v=>v.error))});}
 const groups={};for(const dimension of ['kind','width','yaw']){groups[dimension]=[];for(const value of [...new Set(trials.map(t=>t[dimension]))]){const r=trials.filter(t=>t[dimension]===value);groups[dimension].push({value,n:r.length,arrivals:r.filter(t=>t.arrived).length,complete:r.filter(t=>t.complete).length,falseVisualSuccess:r.filter(t=>t.falseVisualSuccess).length,meanFinalDistance:mean(r.map(t=>t.finalDistance).filter(Number.isFinite)),meanVisionMs:mean(r.map(t=>t.meanVisionMs))});}}
 const summary={schemaVersion:1,subdir,n:trials.length,arrivals:trials.filter(t=>t.arrived).length,stableArrivals:trials.filter(t=>t.stableArrival).length,finalWithinTolerance:trials.filter(t=>t.finalDistance<=1.2).length,complete:trials.filter(t=>t.complete).length,visualDeclarations:trials.filter(t=>t.status==='visual-reached').length,falseVisualSuccess:trials.filter(t=>t.falseVisualSuccess).length,inputEvents:trials.reduce((a,b)=>a+b.inputEvents,0),frames:trials.reduce((a,b)=>a+b.frames,0),allKeysReleased:trials.every(t=>t.heldAfter===0),ballMatchedSamples:allBallErrors.length,actorMatchedSamples:allActorErrors.length,ballMae:mean(allBallErrors),ballRmse:Math.sqrt(mean(allBallErrors.map(e=>e*e))),ballP95:quantile(allBallErrors,.95),actorMae:mean(allActorErrors),actorP95:quantile(allActorErrors,.95),frameAgeMs:{mean:mean(ages),p95:quantile(ages,.95),max:ages.length?Math.max(...ages):null},visionMs:{mean:mean(visionTimes),p95:quantile(visionTimes,.95)},meanDurationSeconds:mean(trials.map(t=>t.seconds)),cpuSeconds:trials.reduce((a,b)=>a+b.cpuSeconds,0),maxFinalWorkingSet:Math.max(...trials.map(t=>t.workingSet)),scoreBins:bins,groups,trials};
 await fs.writeFile(path.join(root,subdir+'-metrics.json'),JSON.stringify(summary,null,2));
 const fields=Object.keys(trials[0]||{});await fs.writeFile(path.join(root,subdir+'-trials.csv'),[fields.join(','),...trials.map(t=>fields.map(f=>JSON.stringify(t[f]??null)).join(','))].join('\n'));
 return summary;
}
