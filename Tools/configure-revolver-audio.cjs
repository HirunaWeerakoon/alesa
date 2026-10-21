const fs=require('fs'),crypto=require('crypto');
function guid(p){return fs.readFileSync(p+'.meta','utf8').match(/^guid: (\w+)/m)[1];}
for(const name of ['RevolverAudio','FiringStopSoundGate']){const p=`Assets/Scripts/${name}.cs`;if(!fs.existsSync(p+'.meta'))fs.writeFileSync(p+'.meta',`fileFormatVersion: 2\nguid: ${crypto.randomUUID().replaceAll('-','')}\n`);}
const clips={emptyClickClip:'Revolver-22-Smith No-Amo_3.wav',reloadClip:'Revolver_Spin.wav',gunshotClip:'Revolver-22-Smith Shot Far_1.wav',shellFallClip:'Small Gun Shell_2.wav'};
for(const file of ['Assets/Scenes/SampleScene.unity','Assets/prefabs/Player.prefab']){
 let scene=fs.readFileSync(file,'utf8');
 const docs=()=>[...scene.matchAll(/^--- !u!(\d+) &(-?\d+)[^\n]*\n([\s\S]*?)(?=^--- |$(?![\s\S]))/gm)];
 const player=docs().find(d=>d[1]==='1'&&/^  m_Name: Player\r?$/m.test(d[3]));if(!player)throw Error('Player missing');
 let audio=docs().find(d=>d[3].includes('Assembly-CSharp::RevolverAudio\n'));
 if(!audio){const ids=new Set(docs().map(d=>d[2]));let id=853000000;while(ids.has(String(id)))id++;
  scene=scene.replace(player[0],player[0].replace('  m_Component:\n',`  m_Component:\n  - component: {fileID: ${id}}\n`));
  scene+=`--- !u!114 &${id}\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: ${player[2]}}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 11500000, guid: ${guid('Assets/Scripts/RevolverAudio.cs')}, type: 3}\n  m_Name: \n  m_EditorClassIdentifier: Assembly-CSharp::RevolverAudio\n`;
  audio=docs().find(d=>d[2]===String(id));
 }
 let body=audio[3];
 for(const [field,name]of Object.entries(clips)){
  const path=`Assets/MaGuns/${name}`;if(!fs.existsSync(path))throw Error('Missing '+path);
  const line=`  ${field}: {fileID: 8300000, guid: ${guid(path)}, type: 3}\n`;
  const pattern=new RegExp('^  '+field+':[^\\n]*\\n','m');body=pattern.test(body)?body.replace(pattern,line):body+line;
 }
 scene=scene.replace(audio[0],audio[0].slice(0,audio[0].length-audio[3].length)+body);
 fs.writeFileSync(file,scene);
}
console.log('Assigned empty click, reload spin, gunshot and delayed shell-fall clips to scene and Player prefab.');
