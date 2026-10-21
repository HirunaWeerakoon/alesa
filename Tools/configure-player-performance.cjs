const fs=require('fs'),crypto=require('crypto');
const script='Assets/Scripts/PlayerLightingBudget.cs';if(!fs.existsSync(script+'.meta'))fs.writeFileSync(script+'.meta',`fileFormatVersion: 2\nguid: ${crypto.randomUUID().replaceAll('-','')}\n`);
const guid=fs.readFileSync(script+'.meta','utf8').match(/guid: (\w+)/)[1];
for(const path of ['Assets/Scenes/SampleScene.unity','Assets/prefabs/Player.prefab']){
 let s=fs.readFileSync(path,'utf8');if(s.includes('Assembly-CSharp::PlayerLightingBudget\n'))continue;
 const docs=[...s.matchAll(/^--- !u!(\d+) &(-?\d+)[^\n]*\n([\s\S]*?)(?=^--- |$(?![\s\S]))/gm)];
 const ids=new Set(docs.map(d=>d[2]));let id=855000000;while(ids.has(String(id)))id++;
 const player=docs.find(d=>d[1]==='1'&&/^  m_Name: Player\r?$/m.test(d[3]));if(!player)throw Error('Player missing');
 s=s.replace(player[0],player[0].replace('  m_Component:\n',`  m_Component:\n  - component: {fileID: ${id}}\n`));
 s+=`--- !u!114 &${id}\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: ${player[2]}}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 11500000, guid: ${guid}, type: 3}\n  m_Name: \n  m_EditorClassIdentifier: Assembly-CSharp::PlayerLightingBudget\n  nearbyShadowLights: 4\n  shadowRadius: 28\n  maximumCustomShadowResolution: 1024\n`;
 fs.writeFileSync(path,s);
}
console.log('Enabled local-light shadow budgeting in scene and Player prefab; flashlight excluded.');
