// Offline setup: import bundled TMP essentials, use the supplied death movie, and wire SampleScene.
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const crypto = require('crypto');
const root = path.resolve(__dirname, '..');
process.chdir(root);
function meta(asset) {
  const file = asset + '.meta';
  if (!fs.existsSync(file)) fs.writeFileSync(file, `fileFormatVersion: 2\nguid: ${crypto.randomUUID().replaceAll('-', '')}\n`);
  return fs.readFileSync(file, 'utf8').match(/guid: (\w+)/)[1];
}

// Unity packages are gzip TAR files. Read only their regular-file payloads.
const cache = 'Library/PackageCache';
const ugui = fs.readdirSync(cache).find(name => name.startsWith('com.unity.ugui@'));
if (!ugui) throw Error('Installed Unity UI package not found');
const tar = zlib.gunzipSync(fs.readFileSync(path.join(cache, ugui, 'Package Resources', 'TMP Essential Resources.unitypackage')));
const entries = new Map();
for (let offset = 0; offset + 512 <= tar.length;) {
  const header = tar.subarray(offset, offset + 512);
  if (header.every(byte => byte === 0)) break;
  const name = header.subarray(0, 100).toString().replace(/\0.*$/, '').replace(/^\.\//, '');
  const size = parseInt(header.subarray(124, 136).toString().replace(/\0.*$/, '').trim() || '0', 8);
  if (!Number.isFinite(size) || offset + 512 + size > tar.length) throw Error('Invalid TMP archive');
  if (header[156] === 48 || header[156] === 0) entries.set(name, tar.subarray(offset + 512, offset + 512 + size));
  offset += 512 + Math.ceil(size / 512) * 512;
}
let imported = 0;
for (const [key, data] of entries) {
  if (!key.endsWith('/pathname')) continue;
  const asset = data.toString().replace(/\0.*$/, '').trim();
  const absolute = path.resolve(root, asset);
  if (!asset.startsWith('Assets/') || !absolute.startsWith(path.join(root, 'Assets') + path.sep)) throw Error('Unsafe TMP asset path');
  const group = key.slice(0, -'pathname'.length);
  const payload = entries.get(group + 'asset');
  if (payload) {
    fs.mkdirSync(path.dirname(absolute), { recursive: true });
    if (!fs.existsSync(absolute)) { fs.writeFileSync(absolute, payload); imported++; }
  } else fs.mkdirSync(absolute, { recursive: true });
  const metadata = entries.get(group + 'asset.meta');
  if (metadata && !fs.existsSync(absolute + '.meta')) fs.writeFileSync(absolute + '.meta', metadata);
}
const font = 'Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset';
if (!fs.existsSync(font)) throw Error('TMP default font was not imported');
const fontGuid = meta(font);

const videoGuid = meta('Assets/animations/death_screen_animation.mp4');
const setupGuid = meta('Assets/Scripts/DeathScreenSceneSetup.cs');
for (const script of ['PlayerDeathSequence', 'DeathScreenUIController', 'DeathVideoPresentation'])
  meta('Assets/Scripts/' + script + '.cs');
const scenePath = 'Assets/Scenes/SampleScene.unity';
let scene = fs.readFileSync(scenePath, 'utf8');
if (/^(<<<<<<<|=======|>>>>>>>)/m.test(scene)) throw Error('Scene has conflict markers');
if (!scene.includes('Assembly-CSharp::DeathScreenSceneSetup')) {
  const docs = [...scene.matchAll(/^--- !u!(\d+) &(-?\d+)[^\n]*\n([\s\S]*?)(?=^--- |$(?![\s\S]))/gm)];
  const player = docs.find(doc => doc[3].includes('Assembly-CSharp::PlayerGameplay\n'));
  if (!player) throw Error('Scene PlayerGameplay not found');
  const ids = new Set(docs.map(doc => doc[2])); let next = 859000000;
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
  m_Name: Death Overlay Setup - Press K in Play Mode
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
  m_Script: {fileID: 11500000, guid: ${setupGuid}, type: 3}
  m_Name: 
  m_EditorClassIdentifier: Assembly-CSharp::DeathScreenSceneSetup
  font: {fileID: 11400000, guid: ${fontGuid}, type: 2}
  player: {fileID: ${player[2]}}
  deathVideo: {fileID: 32900000, guid: ${videoGuid}, type: 3}
`;
  const roots = scene.indexOf('--- !u!1660057539');
  if (roots < 0) throw Error('Scene roots missing');
  scene = scene.slice(0, roots) + object + scene.slice(roots).replace('  m_Roots:\n', `  m_Roots:\n  - {fileID: ${tr}}\n`);
  fs.writeFileSync(scenePath, scene);
}
const settingsPath = 'ProjectSettings/EditorBuildSettings.asset';
let settings = fs.readFileSync(settingsPath, 'utf8');
const sceneGuid = meta(scenePath);
if (!settings.includes('path: ' + scenePath)) {
  const entry = `  - enabled: 1\n    path: ${scenePath}\n    guid: ${sceneGuid}\n`;
  if (settings.includes('  m_Scenes: []')) settings = settings.replace('  m_Scenes: []\n', '  m_Scenes:\n' + entry);
  else settings = settings.replace('  m_Scenes:\n', '  m_Scenes:\n' + entry);
  fs.writeFileSync(settingsPath, settings);
}
console.log(`Configured death overlay, scene retry, and death video. Imported ${imported} bundled TMP assets without overwriting existing files.`);
