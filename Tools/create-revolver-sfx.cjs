// Original procedural close-range revolver report: deterministic, no source samples.
const fs=require('fs'),crypto=require('crypto');
const rate=48000,seconds=1.1,count=Math.round(rate*seconds),samples=new Float64Array(count);
let seed=195021,low=0;
function noise(){seed=(Math.imul(seed,1664525)+1013904223)>>>0;return seed/2147483648-1;}
for(let i=0;i<count;i++){
 const t=i/rate,n=noise();low+=0.065*(n-low);
 const crack=(n-low)*Math.exp(-t/0.014)*1.25;
 const punch=Math.sin(2*Math.PI*(145*t-90*t*t))*Math.exp(-t/0.065)*0.85;
 const body=low*Math.exp(-t/0.16)*1.7+Math.sin(2*Math.PI*68*t)*Math.exp(-t/0.085)*0.45;
 const blast=n*Math.exp(-t/0.055)*0.6;
 samples[i]=Math.tanh((crack+punch+body+blast)*2.1)*Math.min(1,t/0.00025);
}
// Short early room reflections, then a decaying diffuse tail.
const direct=Float64Array.from(samples);
for(const [delay,gain]of [[0.032,.22],[0.067,.14],[0.113,.09],[0.173,.055]]){
 const offset=Math.round(delay*rate);for(let i=offset;i<count;i++)samples[i]+=direct[i-offset]*gain;
}
let peak=0;for(const s of samples)peak=Math.max(peak,Math.abs(s));
const data=Buffer.alloc(count*2),gain=.97/peak;let energy=0;
for(let i=0;i<count;i++){const value=samples[i]*gain;data.writeInt16LE(Math.round(value*32767),i*2);if(i<rate*.25)energy+=value*value;}
const header=Buffer.alloc(44);header.write('RIFF');header.writeUInt32LE(36+data.length,4);header.write('WAVE',8);header.write('fmt ',12);header.writeUInt32LE(16,16);header.writeUInt16LE(1,20);header.writeUInt16LE(1,22);header.writeUInt32LE(rate,24);header.writeUInt32LE(rate*2,28);header.writeUInt16LE(2,32);header.writeUInt16LE(16,34);header.write('data',36);header.writeUInt32LE(data.length,40);
function meta(p,folder=false){if(!fs.existsSync(p+'.meta'))fs.writeFileSync(p+'.meta',`fileFormatVersion: 2\nguid: ${crypto.randomUUID().replaceAll('-','')}\n${folder?'folderAsset: yes\n':''}`);}
fs.mkdirSync('Assets/Audio/SFX',{recursive:true});meta('Assets/Audio',true);meta('Assets/Audio/SFX',true);
const output='Assets/Audio/SFX/Revolver_CloseShot.wav';fs.writeFileSync(output,Buffer.concat([header,data]));meta(output);
console.log(`Created original 48kHz/16-bit mono gunshot; peak -0.26 dBFS; first 250 ms RMS ${(20*Math.log10(Math.sqrt(energy/(rate*.25)))).toFixed(1)} dBFS; no clipped samples.`);
