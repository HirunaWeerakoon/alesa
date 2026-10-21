const fs=require('fs'),crypto=require('crypto');
const script='Assets/Scripts/PickupCabinetPlacement.cs';if(!fs.existsSync(script+'.meta'))fs.writeFileSync(script+'.meta',`fileFormatVersion: 2\nguid: ${crypto.randomUUID().replaceAll('-','')}\n`);
const scriptGuid=fs.readFileSync(script+'.meta','utf8').match(/guid: (\w+)/)[1];
const path='Assets/Scenes/SampleScene.unity';let s=fs.readFileSync(path,'utf8');
const docs=()=>[...s.matchAll(/^--- !u!(\d+) &(-?\d+)[^\n]*\n([\s\S]*?)(?=^--- |$(?![\s\S]))/gm)];
const ids=new Set(docs().map(d=>d[2]));let next=854000000;function id(){while(ids.has(String(next)))next++;ids.add(String(next));return next++;}
function edit(n,fn){const d=docs().find(d=>d[2]===String(n));if(!d)throw Error('Missing document '+n);s=s.replace(d[0],d[0].slice(0,d[0].length-d[3].length)+fn(d[3]));}
function set(body,key,value){const pattern=new RegExp('^  '+key+':[^\\n]*(?:\\n|$)','m');return pattern.test(body)?body.replace(pattern,`  ${key}: ${value}\n`):body+`  ${key}: ${value}\n`;}
const cabinet=docs().find(d=>d[1]==='1001'&&d[3].includes('value: SheetRack (1)\n'));if(!cabinet)throw Error('Starting-room storage cabinet not found');
const cabinetGuid=cabinet[3].match(/m_SourcePrefab: \{fileID: \d+, guid: (\w+)/)[1];
let reference=docs().find(d=>d[1]==='4'&&d[0].includes('stripped')&&d[3].includes(`m_PrefabInstance: {fileID: ${cabinet[2]}}`)&&d[3].includes('fileID: 400000,'));
let cabinetTransform;
if(reference)cabinetTransform=reference[2];else {cabinetTransform=id();s+=`--- !u!4 &${cabinetTransform} stripped\nTransform:\n  m_CorrespondingSourceObject: {fileID: 400000, guid: ${cabinetGuid}, type: 3}\n  m_PrefabInstance: {fileID: ${cabinet[2]}}\n  m_PrefabAsset: {fileID: 0}\n`;}
for(const [name,shelf,position,angles]of [['Revolver pickup','SheetRack','{x: 92.21, y: 26.65, z: -5.5}','{x: 0, y: 0, z: 90}'],['Ammo box (12 rounds)','SheetRack','{x: 92.21, y: 26.73, z: -5.02}','{x: 0, y: 0, z: 0}']]){
 const go=docs().find(d=>d[1]==='1'&&d[3].includes(`m_Name: ${name}\n`));if(!go)throw Error('Missing '+name);
 const tr=docs().find(d=>d[1]==='4'&&d[3].includes(`m_GameObject: {fileID: ${go[2]}}`));
 edit(tr[2],body=>set(set(set(body,'m_LocalPosition',position),'m_LocalRotation',name==='Revolver pickup'?'{x: 0, y: 0, z: 0.7071068, w: 0.7071068}':'{x: 0, y: 0, z: 0, w: 1}'),'m_LocalEulerAnglesHint',angles));
 let component=docs().find(d=>d[3].includes('Assembly-CSharp::PickupCabinetPlacement\n')&&d[3].includes(`m_GameObject: {fileID: ${go[2]}}`));
 if(!component){const n=id();edit(go[2],body=>body.replace('  m_Component:\n',`  m_Component:\n  - component: {fileID: ${n}}\n`));s+=`--- !u!114 &${n}\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: ${go[2]}}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 11500000, guid: ${scriptGuid}, type: 3}\n  m_Name: \n  m_EditorClassIdentifier: Assembly-CSharp::PickupCabinetPlacement\n`;component=docs().find(d=>d[2]===String(n));}
 edit(component[2],body=>set(set(set(set(body,'cabinet',`{fileID: ${cabinetTransform}}`),'shelfName',shelf),'restingAngles',angles),'surfaceOffset',name==='Revolver pickup'?'{x: -0.06, y: 0, z: -0.24}':'{x: -0.06, y: 0, z: 0.24}'));
}
fs.writeFileSync(path,s);
console.log('Moved revolver and ammo side by side onto the first-room cabinet top; runtime bounds snap aligns each visible bottom to the top surface.');
