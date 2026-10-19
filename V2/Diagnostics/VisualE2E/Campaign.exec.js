// JavaScript | Campaign.exec.js | Codex functions.exec tool environment | Finite native-input campaign, per-case checkpoints and resume. No code-edit orchestrator.
// Read computer-use SKILL.md before running. Change reportDirectory for a new campaign; supply its cases.json.
const projectDirectory='C:/Users/luiz/Documents/BlueLockExternal/V2';
const reportDirectory=projectDirectory+'/reports/visual-e2e-20261018';
const maximumCases=50,secondsPerCase=18;
const quote=s=>"'"+s.replace(/'/g,"''")+"'";
async function shell(cmd,max_output_tokens=6000){const r=await tools.exec_command({cmd,max_output_tokens});if(r.exit_code!==0)throw new Error(r.output);return r.output;}
async function read(file){return shell('Get-Content -Raw -LiteralPath '+quote(file));}
async function save(file,value){const r=await tools.apply_patch('*** Begin Patch\n*** Add File: '+file+'\n'+JSON.stringify(value,null,2).split('\n').map(x=>'+'+x).join('\n')+'\n*** End Patch');return r;}
const cases=JSON.parse(await read(reportDirectory+'/cases.json'));
if(cases.length>maximumCases||cases.some(c=>!/^e2e-[0-9]{3}$/.test(c.run)||![480,640,960].includes(c.width)||!Number.isFinite(c.x)||!Number.isFinite(c.z)||!Number.isFinite(c.yaw)||Math.abs(c.x)>15||Math.abs(c.z)>15||!['static','moving','abrupt','occlusion','tracking-loss','perception','resolution'].includes(c.kind)))throw new Error('Invalid or unbounded case plan');
const completed=JSON.parse(await shell('$items=@(Get-ChildItem -Path '+quote(reportDirectory+'/campaign50/*-mcp.json')+' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty BaseName); ConvertTo-Json -InputObject $items -Compress'));
const pending=cases.filter(c=>!completed.includes(c.run+'-mcp'));
text({planned:cases.length,completed:cases.length-pending.length,pending:pending.length});
if(!pending.length){text('Resume check: all cases already have evidence; no Studio or input actions performed.');exit();}
const studios=JSON.parse((await tools.mcp__Roblox_Studio__list_roblox_studios({})).content.find(c=>c.type==='text').text).studios.filter(s=>s.name==='BallControlLab-Running.rbxlx');
if(studios.length!==1)throw new Error('Exactly one BallControlLab-Running.rbxlx required');const studio_id=studios[0].id;
await shell('& '+quote(projectDirectory+'/Diagnostics/VisualE2E/build.ps1')+' -OutputDirectory '+quote(reportDirectory+'/bin'));
await tools.mcp__Roblox_Studio__start_stop_play({studio_id,is_start:false});
const checkpoint=await tools.mcp__Roblox_Studio__execute_luau({studio_id,datamodel_type:'Edit',code:'local a={} for _,s in game:GetDescendants() do if s:IsA("LuaSourceContainer") then table.insert(a,{path=s:GetFullName(),source=s.Source}) end end return game:GetService("HttpService"):JSONEncode(a)'});
await save(reportDirectory+'/resume-live-checkpoint.json',checkpoint);
await shell('New-Item -ItemType Directory -Force -Path '+quote(reportDirectory+'/observer')+' | Out-Null; if(Test-Path -LiteralPath '+quote(reportDirectory+'/observer/STOP-RELAY')+'){throw "Observer stop is latched; use a fresh campaign directory"}; Start-Process -FilePath '+quote(reportDirectory+'/bin/Relay.exe')+' -ArgumentList @('+quote(reportDirectory+'/observer')+",'1200') -WindowStyle Hidden");
try{
 await tools.mcp__Roblox_Studio__start_stop_play({studio_id,is_start:true});
 for(const [file,model] of [['Fixture.client.luau','Client'],['Truth.server.luau','Server']]){const r=await tools.mcp__Roblox_Studio__execute_luau({studio_id,datamodel_type:model,code:await read(projectDirectory+'/Diagnostics/VisualE2E/'+file)});if(r.isError)throw new Error(JSON.stringify(r));}
 const selection=await tools.mcp__node_repl__js({code:"if(!globalThis.sky){globalThis.sky=(await import('@oai/sky')).sky;}globalThis.nativeWindows=(await sky.list_windows()).filter(w=>/BallControlLab-Running.*Roblox Studio/.test(w.title||''));if(nativeWindows.length!==1)throw new Error('Studio window ambiguity');globalThis.campaignWindow=await sky.get_window({id:nativeWindows[0].id,app:nativeWindows[0].app});nodeRepl.write(JSON.stringify(campaignWindow));",title:'Selecionar a janela do laboratório'});
 if(selection.isError)throw new Error(JSON.stringify(selection));
 const template=await read(projectDirectory+'/Diagnostics/VisualE2E/Prepare.template.server.luau');
 for(const c of pending){
  if((await shell('Test-Path -LiteralPath '+quote(reportDirectory+'/campaign50/STOP'))).trim()==='True')break;
  const prepare=await tools.mcp__Roblox_Studio__execute_luau({studio_id,datamodel_type:'Server',code:template.replaceAll('BX',String(c.x)).replaceAll('BZ',String(c.z)).replaceAll('YAW',String(c.yaw)).replaceAll('RUN',c.run).replaceAll('KIND',c.kind)});
  if(prepare.isError)throw new Error(JSON.stringify(prepare));
  const activation=await tools.mcp__node_repl__js({code:'await sky.activate_window({window:campaignWindow});nodeRepl.write("Studio activated");',title:'Focar Studio para '+c.run});
  if(activation.isError){await save(reportDirectory+'/paused.json',{run:c.run,reason:activation});throw new Error('Focus guard paused campaign; reobserve window before resume');}
  let execution=await tools.exec_command({cmd:'& '+quote(reportDirectory+'/bin/Runner.exe')+' '+[reportDirectory+'/campaign50',c.run,String(c.width),String(secondsPerCase),'baseline','relay'].map(quote).join(' '),yield_time_ms:1000,max_output_tokens:1200});
  while(execution.session_id)execution=await tools.write_stdin({session_id:execution.session_id,chars:'',yield_time_ms:1000,max_output_tokens:1200});
  if(execution.exit_code!==0)throw new Error(execution.output);
  const result=JSON.parse(execution.output.trim().split(/\r?\n/).at(-1));
  const final=await tools.mcp__Roblox_Studio__execute_luau({studio_id,datamodel_type:'Server',code:'local p=game.Players:GetPlayers()[1].Character.HumanoidRootPart.Position;local b=workspace.PhysicalBall.Position;return game:GetService("HttpService"):JSONEncode({run=workspace:GetAttribute("VisualRun"),px=p.X,pz=p.Z,bx=b.X,bz=b.Z,distance=Vector2.new(p.X-b.X,p.Z-b.Z).Magnitude})'});
  const truth=JSON.parse(final.content.find(x=>x.type==='text').text);
  await save(reportDirectory+'/campaign50/'+c.run+'-mcp.json',{case:c,prepare,execution,result,final,truth});
  notify({run:c.run,status:result.status,final:truth.distance,heldAfter:result.heldAfter});
  if(result.status==='emergency'||result.heldAfter!==0)break;
 }
}finally{
 await tools.mcp__Roblox_Studio__start_stop_play({studio_id,is_start:false});
 await shell('[IO.File]::WriteAllText('+quote(reportDirectory+'/observer/STOP-RELAY')+",'finite campaign ended')");
}
