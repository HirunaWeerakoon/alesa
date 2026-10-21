const fs=require('fs'),crypto=require('crypto');
const script='Assets/Scripts/PlayerState.cs';if(!fs.existsSync(script+'.meta'))fs.writeFileSync(script+'.meta',`fileFormatVersion: 2\nguid: ${crypto.randomUUID().replaceAll('-','')}\n`);
for(const file of ['Assets/Scenes/SampleScene.unity','Assets/prefabs/Player.prefab']){
 let scene=fs.readFileSync(file,'utf8');
 const docs=[...scene.matchAll(/^--- !u!114 &(-?\d+)[^\n]*\n([\s\S]*?)(?=^--- |$(?![\s\S]))/gm)];
 for(const [name,values]of Object.entries({PlayerMotor:{groundProbeDistance:'0.12',groundStickSpeed:'2'},RevolverCombat:{obstructionRadius:'0.045',obstructionPadding:'0.08',muzzleOffset:'{x: 0, y: 0.045, z: 0.17}',obstructedOffset:'{x: 0, y: -0.12, z: -0.1}'}})){
  const d=docs.find(d=>d[2].includes('Assembly-CSharp::'+name+'\n'));if(!d)throw Error('Missing '+name);
  let body=d[2];
  for(const [key,value]of Object.entries(values)){const pattern=new RegExp('^  '+key+':[^\\n]*\\n','m');const line=`  ${key}: ${value}\n`;body=pattern.test(body)?body.replace(pattern,line):body+line;}
  scene=scene.replace(d[0],d[0].slice(0,d[0].length-d[2].length)+body);
 }
 fs.writeFileSync(file,scene);
}
console.log('Saved grounding and weapon obstruction parameters in scene and Player prefab.');
