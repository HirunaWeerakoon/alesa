const fs=require('fs'),crypto=require('crypto');
function guid(p){return fs.readFileSync(p+'.meta','utf8').match(/^guid: (\w+)/m)[1];}
function meta(p){if(!fs.existsSync(p+'.meta'))fs.writeFileSync(p+'.meta',`fileFormatVersion: 2\nguid: ${crypto.randomUUID().replaceAll('-','')}\n`);return guid(p);}
for(const p of ['Assets/Scripts/RevolverModel.cs','Assets/Scripts/RevolverPickupVisual.cs'])meta(p);
const gunPrefab='Assets/RyanNein/MidPolyGunWeapons/Revolver/Revolver.prefab';
const gunGuid=guid(gunPrefab),textureGuid=guid('Assets/OccaSoftware/Crosshairs/Art/Textures/Crosshair_04.png');
// The package demo explicitly references this prefab's root GameObject ID.
const gunRef=`{fileID: 5911397864059165243, guid: ${gunGuid}, type: 3}`;
const materialPath='Assets/Material/Revolver_Player.mat';
let material=fs.readFileSync('Assets/RyanNein/MidPolyGunWeapons/Revolver/Revolver.mat','utf8').split(/^--- !u!114 /m)[0];
material=material.replace('m_Name: Revolver','m_Name: Revolver_Player').replace(/m_Shader: \{[^\n]+\}/,'m_Shader: {fileID: 46, guid: 0000000000000000f000000000000000, type: 0}').replace('_METALLICSPECGLOSSMAP','_METALLICGLOSSMAP');
fs.writeFileSync(materialPath,material);
const matRef=`{fileID: 2100000, guid: ${meta(materialPath)}, type: 2}`;
function parse(s){return [...s.matchAll(/^--- !u!(\d+) &(-?\d+)[^\n]*\n([\s\S]*?)(?=^--- |$(?![\s\S]))/gm)];}
function set(doc,key,value){const re=new RegExp('^  '+key+':[^\n]*(?:\n|$)','m');return re.test(doc)?doc.replace(re,`  ${key}: ${value}\n`):doc+`  ${key}: ${value}\n`;}
function rewrite(s,id,fn){const re=new RegExp('(^--- !u!\\d+ &'+id+'[^\\n]*\\n)([\\s\\S]*?)(?=^--- |$(?![\\s\\S]))','m');if(!re.test(s))throw Error('Missing document '+id);return s.replace(re,(_,header,body)=>header+fn(body));}
for(const file of ['Assets/Scenes/SampleScene.unity','Assets/prefabs/Player.prefab']){
 let s=fs.readFileSync(file,'utf8');
 let docs=parse(s);const ids=new Set(docs.map(d=>d[2]));let next=851000000;
 const allocate=()=>{while(ids.has(String(next)))next++;ids.add(String(next));return next++;};
 const player=docs.find(d=>d[1]==='1'&&/^  m_Name: Player\r?$/m.test(d[3]));if(!player)throw Error('Player missing in '+file);
 for(const component of ['PlayerGameplay','RevolverCombat']){
  let d=docs.find(d=>d[1]==='114'&&d[3].includes('Assembly-CSharp::'+component+'\n'));
  if(!d){const n=allocate();s=rewrite(s,player[2],body=>body.replace('  m_Component:\n',`  m_Component:\n  - component: {fileID: ${n}}\n`));s+=`--- !u!114 &${n}\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: ${player[2]}}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 11500000, guid: ${guid('Assets/Scripts/'+component+'.cs')}, type: 3}\n  m_Name: \n  m_EditorClassIdentifier: Assembly-CSharp::${component}\n`;d=parse(s).find(d=>d[2]===String(n));}
  s=rewrite(s,d[2],body=>{
   if(component==='PlayerGameplay')return set(body,'crosshairTexture',`{fileID: 2800000, guid: ${textureGuid}, type: 3}`);
   body=set(body,'weaponPrefab',gunRef);body=set(body,'weaponMaterial',matRef);
   for(const [key,value]of Object.entries({modelAngles:'{x: 0, y: 0, z: 0}',loweredPosition:'{x: 0.14, y: -0.16, z: 0.28}',aimedPosition:'{x: 0, y: -0.08, z: 0.24}',loweredAngles:'{x: 5, y: -8, z: -4}',aimedAngles:'{x: 0, y: 0, z: 0}',reloadPosition:'{x: 0.09, y: -0.1, z: 0.29}',reloadAngles:'{x: -12, y: -55, z: 25}',cylinderSwingDistance:'0.055'}))body=set(body,key,value);
   return body;
  });
 }
 if(file.includes('SampleScene')){
  docs=parse(s);const gun=docs.find(d=>d[1]==='1'&&/m_Name: Revolver pickup(?: \(prototype\))?\n/.test(d[3]));
  if(!gun)throw Error('Prototype revolver pickup missing');
  if(!docs.some(d=>d[3].includes('Assembly-CSharp::RevolverPickupVisual\n'))){
   const visual=allocate();
   s=rewrite(s,gun[2],body=>body.replace('  m_Component:\n',`  m_Component:\n  - component: {fileID: ${visual}}\n`).replace('m_Name: Revolver pickup (prototype)','m_Name: Revolver pickup'));
   const tr=docs.find(d=>d[1]==='4'&&d[3].includes(`m_GameObject: {fileID: ${gun[2]}}`));
   s=rewrite(s,tr[2],body=>set(body,'m_LocalScale','{x: 1, y: 1, z: 1}'));
   const box=docs.find(d=>d[1]==='65'&&d[3].includes(`m_GameObject: {fileID: ${gun[2]}}`));
   s=rewrite(s,box[2],body=>set(body,'m_Size','{x: 0.34, y: 0.22, z: 0.22}'));
   s+=`--- !u!114 &${visual}\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: ${gun[2]}}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 11500000, guid: ${guid('Assets/Scripts/RevolverPickupVisual.cs')}, type: 3}\n  m_Name: \n  m_EditorClassIdentifier: Assembly-CSharp::RevolverPickupVisual\n  modelPrefab: ${gunRef}\n  modelMaterial: ${matRef}\n`;
  }
 }
 for(const visual of parse(s).filter(d=>d[3].includes('Assembly-CSharp::RevolverPickupVisual\n')))
  s=rewrite(s,visual[2],body=>set(body,'modelAngles','{x: 0, y: 0, z: 0}'));
 // Apply the requested values to each actual Flashlight, not unrelated scene lights.
 for(const d of parse(s).filter(d=>d[3].includes('Assembly-CSharp::Flashlight\n'))){
  s=rewrite(s,d[2],body=>set(set(set(set(body,'defaultIntensity','1'),'defaultRange','12.5'),'defaultSpotAngle','100'),'defaultColor','{r: 1, g: 0.96, b: 0.88, a: 1}'));
  const lightId=d[3].match(/flashlightLight: \{fileID: (\d+)\}/)?.[1];
  if(!lightId||lightId==='0')continue;
  s=rewrite(s,lightId,body=>{
   for(const [k,v]of Object.entries({m_Type:0,m_Range:12.5,m_SpotAngle:100,m_Intensity:1,m_BounceIntensity:0,m_Lightmapping:4,m_Color:'{r: 1, g: 0.96, b: 0.88, a: 1}'}))body=set(body,k,v);
   body=body.replace(/(  m_Shadows:\n[\s\S]*?    m_Type:) [^\n]+/,'$1 2').replace(/(    m_Resolution:) [^\n]+/,'$1 -1').replace(/(    m_Strength:) [^\n]+/,'$1 1').replace(/(    m_Bias:) [^\n]+/,'$1 0.02');
   return body;
  });
 }
 fs.writeFileSync(file,s);
}
console.log('Configured imported revolver, compatible material, textured crosshair, and flashlight defaults in scene/player prefab.');
