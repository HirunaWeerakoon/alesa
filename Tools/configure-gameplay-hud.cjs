// Adds the runtime Canvas builder to SampleScene without changing existing objects.
const fs = require('fs'), crypto = require('crypto');
function guid(asset) {
  if (!fs.existsSync(asset + '.meta'))
    fs.writeFileSync(asset + '.meta', `fileFormatVersion: 2\nguid: ${crypto.randomUUID().replaceAll('-', '')}\n`);
  return fs.readFileSync(asset + '.meta', 'utf8').match(/guid: (\w+)/)[1];
}
for (const script of ['GameplayHUDController', 'GameplayHUDBridge', 'ECGWaveGraphic', 'GameplayHUDSceneSetup'])
  guid('Assets/Scripts/' + script + '.cs');
const font = 'Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset';
if (!fs.existsSync(font)) throw Error('Import TMP essentials first (Tools/configure-death-screen.cjs)');
const file = 'Assets/Scenes/SampleScene.unity';
let scene = fs.readFileSync(file, 'utf8');
if (/^(<<<<<<<|=======|>>>>>>>)/m.test(scene)) throw Error('Unresolved scene conflicts');
if (scene.includes('Assembly-CSharp::GameplayHUDSceneSetup')) { console.log('Gameplay HUD already configured.'); process.exit(0); }
const docs = [...scene.matchAll(/^--- !u!(\d+) &(-?\d+)[^\n]*\n([\s\S]*?)(?=^--- |$(?![\s\S]))/gm)];
const player = docs.find(doc => doc[3].includes('Assembly-CSharp::PlayerGameplay\n'));
if (!player) throw Error('Missing PlayerGameplay');
const ids = new Set(docs.map(doc => doc[2])); let next = 860000000;
function id() { while (ids.has(String(next))) next++; ids.add(String(next)); return next++; }
const go = id(), tr = id(), component = id();
const object = `--- !u!1 &${go}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: ${tr}}
  - component: {fileID: ${component}}
  m_Layer: 0
  m_Name: Gameplay HUD Setup
  m_TagString: Untagged
  m_Icon: {fileID: 0}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &${tr}
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: ${go}}
  serializedVersion: 2
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {fileID: 0}
  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
--- !u!114 &${component}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: ${go}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: ${guid('Assets/Scripts/GameplayHUDSceneSetup.cs')}, type: 3}
  m_Name: 
  m_EditorClassIdentifier: Assembly-CSharp::GameplayHUDSceneSetup
  font: {fileID: 11400000, guid: ${guid(font)}, type: 2}
  player: {fileID: ${player[2]}}
`;
const roots = scene.indexOf('--- !u!1660057539');
if (roots < 0) throw Error('Missing SceneRoots');
scene = scene.slice(0, roots) + object + scene.slice(roots).replace('  m_Roots:\n', `  m_Roots:\n  - {fileID: ${tr}}\n`);
fs.writeFileSync(file, scene);
console.log('Configured gameplay Canvas HUD in SampleScene.');
