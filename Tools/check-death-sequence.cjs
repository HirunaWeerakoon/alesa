const fs = require('fs'), assert = require('assert');
const scene = fs.readFileSync('Assets/Scenes/SampleScene.unity', 'utf8');
const docs = [...scene.matchAll(/^--- !u!(\d+) &(-?\d+)[^\n]*\n([\s\S]*?)(?=^--- |$(?![\s\S]))/gm)];
const setup = docs.filter(doc => doc[3].includes('Assembly-CSharp::DeathScreenSceneSetup\n'));
assert.equal(setup.length, 1, 'Scene should have exactly one death setup');
const sequenceGuid = fs.readFileSync('Assets/Scripts/PlayerDeathSequence.cs.meta', 'utf8').match(/guid: (\w+)/)[1];
for (const file of ['Assets/Scenes/SampleScene.unity', 'Assets/prefabs/Player.prefab']) {
  const documents = [...fs.readFileSync(file, 'utf8').matchAll(/^--- !u!(\d+) &(-?\d+)[^\n]*\n([\s\S]*?)(?=^--- |$(?![\s\S]))/gm)];
  const player = documents.find(doc => doc[1] === '1' && /^  m_Name: Player\r?$/m.test(doc[3]));
  assert(player, 'Player object missing: ' + file);
  const sequence = documents.filter(doc => doc[3].includes(`guid: ${sequenceGuid},`) && doc[3].includes(`m_GameObject: {fileID: ${player[2]}}`));
  assert.equal(sequence.length, 1, 'Death component must be persisted once on the Player: ' + file);
  assert(player[3].includes(`component: {fileID: ${sequence[0][2]}}`), 'Player must reference death component');
  const camera = sequence[0][3].match(/playerCamera: \{fileID: (-?\d+)\}/);
  assert(camera && documents.some(doc => doc[1] === '20' && doc[2] === camera[1]), 'Death camera must resolve');
  assert(sequence[0][3].includes('enableDebugKey: 1\n') && sequence[0][3].includes('debugKey: 25\n'), 'K binding must be enabled');
}
const videoGuid = fs.readFileSync('Assets/animations/death_screen_animation.mp4.meta','utf8').match(/guid: (\w+)/)[1];
assert(setup[0][3].includes('deathVideo: {fileID: 32900000, guid: ' + videoGuid), 'Death video must be assigned');
assert(!/flatlineTone:|floorFlashlightPrefab:|monochromeShader:/.test(setup[0][3]), 'Legacy presentation must be disconnected');
assert(fs.statSync('Assets/animations/death_screen_animation.mp4').size > 0, 'Death video must exist');
const settings = fs.readFileSync('ProjectSettings/EditorBuildSettings.asset', 'utf8');
assert(/enabled: 1\s+path: Assets\/Scenes\/SampleScene.unity/.test(settings), 'Retry scene must be enabled');
console.log('Passed death video binding, Player death/K wiring, legacy disconnection, and retry scene checks.');
