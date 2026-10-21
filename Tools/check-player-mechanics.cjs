const fs=require('fs');
const assert=require('assert');
const input=JSON.parse(fs.readFileSync('Assets/inputs/PlayerInputs.inputactions','utf8'));
const map=input.maps.find(m=>m.name==='OnFoot');
for(const [name,binding]of [['Interact','<Keyboard>/e'],['Aim','<Mouse>/rightButton'],['Shoot','<Mouse>/leftButton'],['Reload','<Keyboard>/r']]){
 assert(map.actions.some(a=>a.name===name&&a.type==='Button'),name+' action missing');
 assert(map.bindings.some(b=>b.action===name&&b.path===binding),name+' binding missing');
}
const scene=fs.readFileSync('Assets/Scenes/SampleScene.unity','utf8');
assert(!/^(<<<<<<<|=======|>>>>>>>)/m.test(scene),'Merge markers remain');
const docs=[...scene.matchAll(/^--- !u!(\d+) &(-?\d+)[^\n]*\n([\s\S]*?)(?=^--- |$(?![\s\S]))/gm)];
const ids=new Set(docs.map(m=>m[2]));
assert.equal(ids.size,docs.length,'Duplicate Unity object IDs');
const goDocs=docs.filter(m=>m[1]==='1');
const player=goDocs.find(m=>/^  m_Name: Player$/m.test(m[3]));
assert(player,'Player GameObject missing');
for(const name of ['PlayerGameplay','RevolverCombat','InteractablePickup','MaintenanceBreaker','DialpadKeypad','DamageableTarget']){
 const guid=fs.readFileSync(`Assets/Scripts/${name}.cs.meta`,'utf8').match(/guid: (\w+)/)[1];
 assert(scene.includes(`guid: ${guid}, type: 3`),name+' is not wired into the scene');
 if(name==='PlayerGameplay'||name==='RevolverCombat'){
  const component=docs.find(m=>m[3].includes(`guid: ${guid}, type: 3`));
  assert(component[3].includes(`m_GameObject: {fileID: ${player[2]}}`));
  assert(player[3].includes(`component: {fileID: ${component[2]}}`));
 }
}
const root=goDocs.find(m=>m[3].includes('m_Name: Player mechanics prototype\n'));
assert(root,'Prototype group missing');
for(const d of docs.filter(m=>+m[2]>=850000000&&+m[2]<852000000)){
 for(const line of d[3].split('\n')){
  if(line.includes('guid:'))continue;
  for(const ref of line.matchAll(/fileID: (-?\d+)/g))assert(ref[1]==='0'||ids.has(ref[1]),`Missing local reference ${ref[1]} in ${d[2]}`);
 }
}
console.log('Passed input bindings, component wiring, unique scene IDs and prototype reference checks.');
if(fs.existsSync('Assets/Audio/SFX/Revolver_CloseShot.wav')){
 const wav=fs.readFileSync('Assets/Audio/SFX/Revolver_CloseShot.wav');
 assert(wav.toString('ascii',0,4)==='RIFF'&&wav.toString('ascii',8,12)==='WAVE','Generated gunshot must be a WAV');
 let peak=0,energy=0;for(let i=44;i<wav.length;i+=2){const value=wav.readInt16LE(i)/32768;peak=Math.max(peak,Math.abs(value));if(i<44+24000)energy+=value*value;}
 assert(peak>0.9&&peak<0.999,'Generated gunshot must use headroom without clipping');
 assert(Math.sqrt(energy/12000)>0.2,'Close gunshot needs a strong initial report');
 const placements=docs.filter(d=>d[3].includes('Assembly-CSharp::PickupCabinetPlacement\n'));
 assert.equal(placements.length,2,'Revolver and ammo need cabinet placement');
 assert(placements.every(d=>d[3].includes('shelfName: SheetRack\n')),'Pickups must use the cabinet top');
 assert(placements.some(d=>d[3].includes('z: -0.24}'))&&placements.some(d=>d[3].includes('z: 0.24}')),'Pickups need separate positions on the cabinet top');
 for(const d of placements){const cabinet=d[3].match(/cabinet: \{fileID: (\d+)\}/)[1];assert(ids.has(cabinet),'Cabinet reference missing');}
 assert(scene.includes('Assembly-CSharp::PlayerLightingBudget\n'),'Shadow budget missing');
 console.log('Passed generated gunshot peak/RMS, adjacent cabinet shelf bindings and lighting-budget checks.');
}
if(fs.existsSync('Assets/Scripts/RevolverAudio.cs.meta')){
 for(const file of ['Assets/Scenes/SampleScene.unity','Assets/prefabs/Player.prefab']){
  const s=fs.readFileSync(file,'utf8');
  const audio=[...s.matchAll(/^--- !u!114 &[^\n]+\n([\s\S]*?)(?=^--- |$(?![\s\S]))/gm)].find(m=>m[1].includes('Assembly-CSharp::RevolverAudio\n'))?.[1];
  assert(audio,'Missing revolver audio in '+file);
  for(const [field,name]of Object.entries({emptyClickClip:'Assets/MaGuns/Revolver-22-Smith No-Amo_3.wav',reloadClip:'Assets/MaGuns/Revolver_Spin.wav',gunshotClip:'Assets/MaGuns/Revolver-22-Smith Shot Far_1.wav',shellFallClip:'Assets/MaGuns/Small Gun Shell_2.wav'})){
   const guid=fs.readFileSync(name+'.meta','utf8').match(/^guid: (\w+)/m)[1];
   assert(audio.includes(`${field}: {fileID: 8300000, guid: ${guid}, type: 3}`),'Wrong revolver '+field);
  }
 }
 console.log('Passed all four revolver audio references in scene and Player prefab.');
}
if(fs.existsSync('Assets/Scripts/PlayerMovementAudio.cs.meta')){
 const audioGuid=p=>fs.readFileSync(p+'.meta','utf8').match(/^guid: (\w+)/m)[1];
 const base='Assets/Footsteps - Essentials/Footsteps_Tile';
 const walk=n=>`${base}/Footsteps_Tile_Walk/Footsteps_Tile_Walk_${String(n).padStart(2,'0')}.wav`;
 const sequences={walkClips:[4,5,6,7].map(walk),crouchClips:[8,1,2,3].map(walk),runClips:[1,2,3,4,5,6].map(n=>`${base}/Footsteps_Tile_Run/Footsteps_Tile_Run_${String(n).padStart(2,'0')}.wav`)};
 for(const file of ['Assets/Scenes/SampleScene.unity','Assets/prefabs/Player.prefab']){
  const s=fs.readFileSync(file,'utf8');
  const audio=[...s.matchAll(/^--- !u!114 &[^\n]+\n([\s\S]*?)(?=^--- |$(?![\s\S]))/gm)].find(m=>m[1].includes('Assembly-CSharp::PlayerMovementAudio\n'))?.[1];
  assert(audio,'Player movement audio missing in '+file);
  for(const [key,paths]of Object.entries(sequences)){
   const refs=audio.match(new RegExp('^  '+key+':\\n((?:  - [^\\n]*\\n)*)','m'))?.[1];
   assert(refs,'Missing '+key);
   assert.deepEqual([...refs.matchAll(/guid: (\w+)/g)].map(m=>m[1]),paths.map(audioGuid),'Wrong clip order for '+key+' in '+file);
  }
  for(const [field,name]of [['jumpClip','Footsteps_Tile_Jump_Start_01'],['landClip','Footsteps_Tile_Jump_Land_01'],['paperClip','Footsteps_Tile_Jump_Start_03'],['weaponPickupClip','Footsteps_Tile_Jump_Start_02'],['ammoPickupClip','Footsteps_Tile_Jump_Start_02']])assert(audio.includes(`${field}: {fileID: 8300000, guid: ${audioGuid(`${base}/Footsteps_Tile_Jump/${name}.wav`)}, type: 3}`),'Wrong '+field);
 }
 console.log('Passed exact walking/crouching/running clip order and jump/paper references in scene and Player prefab.');
}
const revolverAsset='Assets/RyanNein/MidPolyGunWeapons/Revolver/Revolver.prefab';
if(fs.existsSync(revolverAsset)){
 const guid=p=>fs.readFileSync(p+'.meta','utf8').match(/^guid: (\w+)/m)[1];
 const weapon=docs.find(d=>d[3].includes('Assembly-CSharp::RevolverCombat\n'));
 assert(weapon[3].includes(`weaponPrefab: {fileID: 5911397864059165243, guid: ${guid(revolverAsset)}, type: 3}`),'Imported revolver not assigned');
 for(const source of [scene,fs.readFileSync('Assets/prefabs/Player.prefab','utf8')]){
  const presentation=[...source.matchAll(/^--- !u!114 &[^\n]+\n([\s\S]*?)(?=^--- |$(?![\s\S]))/gm)].find(m=>m[1].includes('Assembly-CSharp::RevolverCombat\n'))?.[1];
  assert(presentation,'Weapon presentation component missing');
  assert(presentation.includes('modelAngles: {x: 0, y: 0, z: 0}'),'Revolver must face forward without the reversed yaw');
  assert(presentation.includes('loweredPosition: {x: 0.14, y: -0.16, z: 0.28}'),'Close resting pose not saved');
  assert(presentation.includes('aimedPosition: {x: 0, y: -0.08, z: 0.24}'),'Centred aim pose not saved');
  assert(presentation.includes('reloadAngles: {x: -12, y: -55, z: 25}'),'Side-on reload pose not saved');
 }
 const gameplay=docs.find(d=>d[3].includes('Assembly-CSharp::PlayerGameplay\n'));
 assert(gameplay[3].includes(`crosshairTexture: {fileID: 2800000, guid: ${guid('Assets/OccaSoftware/Crosshairs/Art/Textures/Crosshair_04.png')}, type: 3}`),'Crosshair texture not assigned');
 const flashlight=docs.find(d=>d[3].includes('Assembly-CSharp::Flashlight\n'));
 assert(/^  defaultIntensity: 1$/m.test(flashlight[3]),'Flashlight script intensity');
 const lightId=flashlight[3].match(/flashlightLight: \{fileID: (\d+)\}/)[1];
 const light=docs.find(d=>d[2]===lightId)[3];
 for(const [key,value]of Object.entries({m_Intensity:1,m_Range:12.5,m_SpotAngle:100,m_BounceIntensity:0,m_Lightmapping:4}))assert(light.includes(`  ${key}: ${value}\n`),'Flashlight '+key);
 const material=fs.readFileSync('Assets/Material/Revolver_Player.mat','utf8');
 assert(material.includes('m_Shader: {fileID: 46,'),'Revolver must use the project Standard shader');
 console.log('Passed imported revolver, crosshair, compatible material and flashlight defaults checks.');
}
