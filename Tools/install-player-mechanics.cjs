// Run from the project root. Adds a playable prototype area without replacing scene content.
const fs=require('fs'),crypto=require('crypto');
const files=['PlayerGameplay','RevolverCombat','RevolverAmmo','IInteractable','IDamageable','InteractablePickup','MaintenanceBreaker','DialpadKeypad','DamageableTarget'];
const guids={};
for(const name of files){const p=`Assets/Scripts/${name}.cs.meta`;if(!fs.existsSync(p))fs.writeFileSync(p,`fileFormatVersion: 2\nguid: ${crypto.randomUUID().replaceAll('-','')}\n`);guids[name]=fs.readFileSync(p,'utf8').match(/guid: (\w+)/)[1];}
const inputPath='Assets/inputs/PlayerInputs.inputactions';
const input=JSON.parse(fs.readFileSync(inputPath,'utf8')),foot=input.maps.find(m=>m.name==='OnFoot');
for(const [name,binding]of [['Interact','<Keyboard>/e'],['Aim','<Mouse>/rightButton'],['Shoot','<Mouse>/leftButton'],['Reload','<Keyboard>/r']]){
 if(!foot.actions.some(a=>a.name===name))foot.actions.push({name,type:'Button',id:crypto.randomUUID(),expectedControlType:'Button',processors:'',interactions:'',initialStateCheck:false});
 if(!foot.bindings.some(b=>b.action===name))foot.bindings.push({name:'',id:crypto.randomUUID(),path:binding,interactions:'',processors:'',groups:'',action:name,isComposite:false,isPartOfComposite:false});
}
fs.writeFileSync(inputPath,JSON.stringify(input,null,4)+'\n');
const scenePath='Assets/Scenes/SampleScene.unity';
let scene=fs.readFileSync(scenePath,'utf8');
if(scene.includes('m_Name: Player mechanics prototype')){console.log('Prototype already installed; input actions updated.');process.exit(0);}
let next=850000000;const ids=new Set([...scene.matchAll(/^--- !u!\d+ &(-?\d+)/gm)].map(m=>m[1]));
const id=()=>{while(ids.has(String(next)))next++;ids.add(String(next));return next++;};
const docs=[];
const vector=a=>`{x: ${a[0]}, y: ${a[1]}, z: ${a[2]}}`;
function doc(type,n,body){docs.push(`--- !u!${type} &${n}\n${body}\n`);}
function obj(name,p,scale=[1,1,1],parent=0){const go=id(),tr=id();const o={go,tr,name,p,scale,parent,components:[tr],children:[]};return o;}
function mono(o,name,fields=''){const n=id();o.components.push(n);doc(114,n,`MonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: ${o.go}}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 11500000, guid: ${guids[name]}, type: 3}\n  m_Name: \n  m_EditorClassIdentifier: Assembly-CSharp::${name}\n${fields}`);return n;}
function emit(o){doc(1,o.go,`GameObject:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  serializedVersion: 6\n  m_Component:\n${o.components.map(n=>`  - component: {fileID: ${n}}`).join('\n')}\n  m_Layer: 0\n  m_Name: ${o.name}\n  m_TagString: Untagged\n  m_IsActive: 1`);doc(4,o.tr,`Transform:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: ${o.go}}\n  serializedVersion: 2\n  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}\n  m_LocalPosition: ${vector(o.p)}\n  m_LocalScale: ${vector(o.scale)}\n  m_Children: ${o.children.length?'\n'+o.children.map(n=>`  - {fileID: ${n}}`).join('\n'):'[]'}\n  m_Father: {fileID: ${o.parent}}\n  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}`);}
function cube(o){const mesh=id(),render=id(),collider=id();o.components.push(mesh,render,collider);doc(33,mesh,`MeshFilter:\n  m_GameObject: {fileID: ${o.go}}\n  m_Mesh: {fileID: 10202, guid: 0000000000000000e000000000000000, type: 0}`);doc(23,render,`MeshRenderer:\n  m_GameObject: {fileID: ${o.go}}\n  m_Enabled: 1\n  m_CastShadows: 1\n  m_ReceiveShadows: 1\n  m_Materials:\n  - {fileID: 10303, guid: 0000000000000000f000000000000000, type: 0}\n  m_SortingLayerID: 0\n  m_SortingOrder: 0`);doc(65,collider,`BoxCollider:\n  m_GameObject: {fileID: ${o.go}}\n  m_Enabled: 1\n  m_IsTrigger: 0\n  serializedVersion: 3\n  m_Size: {x: 1, y: 1, z: 1}\n  m_Center: {x: 0, y: 0, z: 0}`);}
const root=obj('Player mechanics prototype',[0,0,0]);
function item(name,p,scale){const o=obj(name,p,scale,root.tr);root.children.push(o.tr);cube(o);return o;}
// Player components are serialized so their settings are visible in the Inspector.
// Match the individual document, rather than spanning earlier GameObjects.
const goDocs=[...scene.matchAll(/^--- !u!1 &(\d+)\r?\n([\s\S]*?)(?=^--- |$(?![\s\S]))/gm)];
const pg=goDocs.find(m=>/^  m_Name: Player\r?$/m.test(m[2]));if(!pg)throw Error('Player missing');
const player={go:pg[1],components:[]};
const gameplay=mono(player,'PlayerGameplay','  interactionDistance: 2\n  interactionMask:\n    serializedVersion: 2\n    m_Bits: 4294967295');
const combat=mono(player,'RevolverCombat','  ammo:\n    capacity: 6\n    loaded: 0\n    spare: 0\n  hasRevolver: 0\n  reloadSeconds: 2\n  shotInterval: 0.35\n  damage: 35\n  range: 60\n  hitMask:\n    serializedVersion: 2\n    m_Bits: 4294967295');
scene=scene.replace(pg[0],pg[0].replace('  m_Component:\n',`  m_Component:\n  - component: {fileID: ${gameplay}}\n  - component: {fileID: ${combat}}\n`));
const gun=item('Revolver pickup (prototype)',[81.7,25.85,-2.18],[0.32,0.18,0.16]);mono(gun,'InteractablePickup','  kind: 0\n  rounds: 6');emit(gun);
const ammo=item('Ammo box (12 rounds)',[82,25.15,-0.7],[0.3,0.22,0.22]);mono(ammo,'InteractablePickup','  kind: 1\n  rounds: 12');emit(ammo);
const note=item('Note - maintenance access clue',[83.5,25.15,0.5],[0.35,0.03,0.26]);mono(note,'InteractablePickup','  kind: 2\n  rounds: 0\n  noteTitle: Maintenance access\n  noteText: "Restore the maintenance breaker first. The access code is 1950. Use the keypad to release Alesa."');emit(note);
const light=obj('Restored waiting-area light',[85.5,28,1.5],[1,1,1],root.tr);root.children.push(light.tr);const lightId=id();light.components.push(lightId);doc(108,lightId,`Light:\n  m_GameObject: {fileID: ${light.go}}\n  m_Enabled: 0\n  serializedVersion: 12\n  m_Type: 2\n  m_Color: {r: 1, g: 0.86, b: 0.65, a: 1}\n  m_Intensity: 1\n  m_Range: 8\n  m_Shadows:\n    m_Type: 0\n  m_CullingMask:\n    serializedVersion: 2\n    m_Bits: 4294967295\n  m_RenderingLayerMask: 1\n  m_Lightmapping: 4`);emit(light);
const breaker=item('Maintenance breaker (prototype)',[85,25.9,3],[0.4,0.6,0.2]);const breakerId=mono(breaker,'MaintenanceBreaker',`  restoredLights:\n  - {fileID: ${lightId}}\n  poweredObjects: []`);emit(breaker);
const barrier=item('Alesa access barrier (prototype)',[89,25.62,1],[0.15,2,1]);emit(barrier);
const pad=item('Alesa access keypad (prototype)',[87,25.9,3],[0.35,0.45,0.15]);
const camera=obj('Keypad stationary camera',[87,26,1.7],[1,1,1],root.tr);root.children.push(camera.tr);const cameraId=id();camera.components.push(cameraId);doc(20,cameraId,`Camera:\n  m_GameObject: {fileID: ${camera.go}}\n  m_Enabled: 0\n  serializedVersion: 2\n  m_ClearFlags: 1\n  m_BackGroundColor: {r: 0.02, g: 0.02, b: 0.02, a: 1}\n  near clip plane: 0.03\n  far clip plane: 1000\n  field of view: 50\n  orthographic: 0\n  m_Depth: 1\n  m_NormalizedViewPortRect:\n    serializedVersion: 2\n    x: 0\n    y: 0\n    width: 1\n    height: 1\n  m_CullingMask:\n    serializedVersion: 2\n    m_Bits: 4294967295\n  m_TargetDisplay: 0\n  m_HDR: 1\n  m_AllowMSAA: 1`);emit(camera);
mono(pad,'DialpadKeypad',`  accessCode: 1950\n  stationaryCamera: {fileID: ${cameraId}}\n  requiredBreaker: {fileID: ${breakerId}}\n  lockedBarrier: {fileID: ${barrier.go}}`);emit(pad);
const target=item('Combat target (prototype)',[90,25.65,-3],[0.5,1.3,0.4]);mono(target,'DamageableTarget','  health: 100');emit(target);
emit(root);
const rootHeader=/^--- !u!1660057539 &[^\n]+\n/m;
if(!rootHeader.test(scene))throw Error('Missing SceneRoots');
scene=scene.replace(rootHeader,docs.join('')+'$&');
scene=scene.replace(/(SceneRoots:\n[\s\S]*?  m_Roots:)/,`$1\n  - {fileID: ${root.tr}}`);
fs.writeFileSync(scenePath,scene);
console.log('Installed player components, pickups, breaker light, keypad camera, release barrier and combat target.');
