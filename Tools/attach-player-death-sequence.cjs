// Persist the death component and camera reference on both existing player assets.
const fs = require('fs');
const guid = fs.readFileSync('Assets/Scripts/PlayerDeathSequence.cs.meta', 'utf8').match(/guid: (\w+)/)[1];
const cache = fs.readdirSync('Library/PackageCache').find(name => name.startsWith('com.unity.inputsystem@'));
const keys = fs.readFileSync(`Library/PackageCache/${cache}/InputSystem/Runtime/Devices/Keyboard.cs`, 'utf8')
  .match(/public enum Key\s*\{([\s\S]*?)\n    \}/)[1].replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/[^\n]*/g, '');
let keyValue = -1, debugKey;
for (const item of keys.split(',')) {
  const [name, value] = item.trim().split(/\s*=\s*/);
  keyValue = value === undefined ? keyValue + 1 : Number(value);
  if (!Number.isInteger(keyValue)) throw Error('Unsupported key enum assignment');
  if (name === 'K') { debugKey = keyValue; break; }
}
if (debugKey === undefined) throw Error('Input System Key.K not found');

for (const file of ['Assets/Scenes/SampleScene.unity', 'Assets/prefabs/Player.prefab']) {
  let text = fs.readFileSync(file, 'utf8');
  if (/^(<<<<<<<|=======|>>>>>>>)/m.test(text)) throw Error('Unresolved conflicts: ' + file);
  const docs = [...text.matchAll(/^--- !u!(\d+) &(-?\d+)[^\n]*\n([\s\S]*?)(?=^--- |$(?![\s\S]))/gm)];
  const player = docs.find(d => d[1] === '1' && /^  m_Name: Player\r?$/m.test(d[3]));
  if (!player) throw Error('Missing player: ' + file);
  const motor = docs.find(d => d[3].includes('Assembly-CSharp::PlayerMotor\n') && d[3].includes(`m_GameObject: {fileID: ${player[2]}}`));
  let camera = motor && motor[3].match(/playerCamera: \{fileID: (-?\d+)\}/);
  if (!camera) {
    const cameraGO = docs.find(d => d[1] === '1' && /^  m_Name: Main Camera\r?$/m.test(d[3]));
    const cameraDoc = cameraGO && docs.find(d => d[1] === '20' && d[3].includes(`m_GameObject: {fileID: ${cameraGO[2]}}`));
    if (cameraDoc) camera = [null, cameraDoc[2]];
  }
  if (!camera || !docs.some(d => d[1] === '20' && d[2] === camera[1])) throw Error('Missing assigned player camera: ' + file);
  if (docs.some(d => d[3].includes(`m_Script: {fileID: 11500000, guid: ${guid},`) && d[3].includes(`m_GameObject: {fileID: ${player[2]}}`))) {
    console.log('Player death component already attached: ' + file); continue;
  }
  const ids = new Set(docs.map(d => d[2])); let id = 861000000;
  while (ids.has(String(id))) id++;
  text = text.replace(player[0], player[0].replace('  m_Component:\n', `  m_Component:\n  - component: {fileID: ${id}}\n`));
  const component = `--- !u!114 &${id}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: ${player[2]}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: ${guid}, type: 3}
  m_Name: 
  m_EditorClassIdentifier: Assembly-CSharp::PlayerDeathSequence
  playerCamera: {fileID: ${camera[1]}}
  deathUI: {fileID: 0}
  collapsedCameraAnchor: {fileID: 0}
  heldObjectsToHide: []
  audioToStop: []
  extraControllersToDisable: []
  collapseDuration: 0.65
  floorEyeHeight: 0.2
  rollDegrees: 20
  upwardPitch: -3
  floorFlashlight: {fileID: 0}
  silhouette: {fileID: 0}
  generatedStage: {fileID: 0}
  deathVolume: {fileID: 0}
  monochrome: {fileID: 0}
  veins: {fileID: 0}
  enableDebugKey: 1
  debugKey: ${debugKey}
`;
  const roots = text.indexOf('--- !u!1660057539');
  text = roots < 0 ? text.trimEnd() + '\n' + component : text.slice(0, roots) + component + text.slice(roots);
  fs.writeFileSync(file, text);
  console.log(`Attached PlayerDeathSequence with camera assigned and Key.K (${debugKey}): ${file}`);
}
