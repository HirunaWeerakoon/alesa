const fs=require('fs'),crypto=require('crypto');
const script='Assets/Scripts/PlayerMovementAudio.cs';
if(!fs.existsSync(script+'.meta'))fs.writeFileSync(script+'.meta',`fileFormatVersion: 2\nguid: ${crypto.randomUUID().replaceAll('-','')}\n`);
const guid=p=>fs.readFileSync(p+'.meta','utf8').match(/^guid: (\w+)/m)[1];
const base='Assets/Footsteps - Essentials/Footsteps_Tile';
const walk=n=>`${base}/Footsteps_Tile_Walk/Footsteps_Tile_Walk_${String(n).padStart(2,'0')}.wav`;
const run=fs.readdirSync(`${base}/Footsteps_Tile_Run`).filter(n=>n.endsWith('.wav')).sort().map(n=>`${base}/Footsteps_Tile_Run/${n}`);
const clips={walkClips:[4,5,6,7].map(walk),runClips:run,crouchClips:[8,1,2,3].map(walk)};
const singles={jumpClip:`${base}/Footsteps_Tile_Jump/Footsteps_Tile_Jump_Start_01.wav`,landClip:`${base}/Footsteps_Tile_Jump/Footsteps_Tile_Jump_Land_01.wav`,paperClip:`${base}/Footsteps_Tile_Jump/Footsteps_Tile_Jump_Start_03.wav`,weaponPickupClip:`${base}/Footsteps_Tile_Jump/Footsteps_Tile_Jump_Start_02.wav`,ammoPickupClip:`${base}/Footsteps_Tile_Jump/Footsteps_Tile_Jump_Start_02.wav`};
function audioRef(p){if(!fs.existsSync(p))throw Error('Missing audio: '+p);return `{fileID: 8300000, guid: ${guid(p)}, type: 3}`;}
for(const file of ['Assets/Scenes/SampleScene.unity','Assets/prefabs/Player.prefab']){
 let scene=fs.readFileSync(file,'utf8');
 const docs=()=>[...scene.matchAll(/^--- !u!(\d+) &(-?\d+)[^\n]*\n([\s\S]*?)(?=^--- |$(?![\s\S]))/gm)];
 const player=docs().find(d=>d[1]==='1'&&/^  m_Name: Player\r?$/m.test(d[3]));if(!player)throw Error('Player missing');
 let component=docs().find(d=>d[3].includes('Assembly-CSharp::PlayerMovementAudio\n'));
 if(!component){
  const ids=new Set(docs().map(d=>d[2]));let id=852000000;while(ids.has(String(id)))id++;
  scene=scene.replace(player[0],player[0].replace('  m_Component:\n',`  m_Component:\n  - component: {fileID: ${id}}\n`));
  scene+=`--- !u!114 &${id}\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: ${player[2]}}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 11500000, guid: ${guid(script)}, type: 3}\n  m_Name: \n  m_EditorClassIdentifier: Assembly-CSharp::PlayerMovementAudio\n`;
  component=docs().find(d=>d[2]===String(id));
 }
 let body=component[3];
 for(const [key,paths]of Object.entries(clips)){
  const block=`  ${key}:\n${paths.map(p=>'  - '+audioRef(p)).join('\n')}\n`;
  const pattern=new RegExp('^  '+key+':[^\\n]*\\n(?:  - [^\\n]*\\n)*','m');
  body=pattern.test(body)?body.replace(pattern,block):body+block;
 }
 for(const [key,path]of Object.entries(singles)){
  const line=`  ${key}: ${audioRef(path)}\n`,re=new RegExp('^  '+key+':[^\\n]*(?:\\n|$)','m');
  body=re.test(body)?body.replace(re,line):body+line;
 }
 scene=scene.replace(component[0],component[0].slice(0,component[0].length-component[3].length)+body);
 fs.writeFileSync(file,scene);
}
console.log('Assigned movement, jump, paper, weapon pickup and ammo pickup sounds to scene and Player prefab.');
