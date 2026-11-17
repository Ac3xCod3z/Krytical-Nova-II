import * as THREE from 'three';

// Look around a fixed camera position; translation is a separate gesture.
export class PivotControls {
 constructor(camera,element){
  this.camera=camera;this.target=new THREE.Vector3();this.points=new Map();
  element.style.touchAction='none';
  element.addEventListener('contextmenu',e=>e.preventDefault());
  element.addEventListener('pointerdown',e=>{
   if(e.pointerType==='mouse'&&e.button!==0&&e.button!==2)return;
   this.points.set(e.pointerId,{x:e.clientX,y:e.clientY,pan:e.button===2||e.shiftKey});
   element.setPointerCapture(e.pointerId);
  });
  element.addEventListener('pointermove',e=>{
   const old=this.points.get(e.pointerId);if(!old)return;
   const before=[...this.points.values()].map(p=>({...p}));
   const dx=e.clientX-old.x,dy=e.clientY-old.y;
   old.x=e.clientX;old.y=e.clientY;
   if(this.points.size===1){
    if(old.pan)this.pan(dx,dy,element.clientHeight);
    else this.look(dx,dy);
   }else if(this.points.size===2){
    const after=[...this.points.values()];
    this.pan(dx/2,dy/2,element.clientHeight);
    const distance=p=>Math.hypot(p[0].x-p[1].x,p[0].y-p[1].y);
    const a=distance(before),b=distance(after);
    if(a>1&&b>1)this.dolly(Math.log(b/a));
   }
  });
  for(const event of ['pointerup','pointercancel','lostpointercapture'])element.addEventListener(event,e=>this.points.delete(e.pointerId));
  window.addEventListener('blur',()=>this.points.clear());
 }
 update(){this.camera.lookAt(this.target);}
 look(dx,dy){
  const distance=Math.max(.1,this.camera.position.distanceTo(this.target));
  const angles=new THREE.Euler().setFromQuaternion(this.camera.quaternion,'YXZ');
  angles.y-=dx*.003;angles.x=THREE.MathUtils.clamp(angles.x-dy*.003,-Math.PI/2+.002,Math.PI/2-.002);angles.z=0;
  const direction=new THREE.Vector3(0,0,-1).applyEuler(angles);
  this.target.copy(this.camera.position).addScaledVector(direction,distance);
  this.update();
 }
 pan(dx,dy,height){
  const scale=2*Math.tan(THREE.MathUtils.degToRad(this.camera.fov/2))*Math.max(2,Math.min(150,this.camera.position.distanceTo(this.target)))/Math.max(1,height);
  const delta=new THREE.Vector3(-dx*scale,dy*scale,0).applyQuaternion(this.camera.quaternion);
  this.camera.position.add(delta);this.target.add(delta);this.update();
 }
 dolly(amount){
  const delta=this.camera.getWorldDirection(new THREE.Vector3()).multiplyScalar(amount*Math.max(2,Math.min(150,this.camera.position.distanceTo(this.target))));
  this.camera.position.add(delta);this.target.add(delta);this.update();
 }
}
