import * as THREE from 'three';
import {mergeGeometries} from '../viewer-lib/BufferGeometryUtils.js';

// Batch static opaque geometry without changing positions, materials or transparent panels.
export function optimizeReview(root){
 root.updateMatrixWorld(true);
 const inverse=root.matrixWorld.clone().invert(),groups=new Map(),originalGeometries=new Set();
 let before=0;
 root.traverse(o=>{
  if(!o.isMesh)return;before++; let p=o; while(p){if(p.name.startsWith("GateLeaf")||p.name.startsWith("SkyDome")||p.name.startsWith("SkyCube"))return;p=p.parent;}
  const m=o.material,g=o.geometry;
  if(o.children.length||o.isSkinnedMesh||o.isInstancedMesh||Array.isArray(m)||m.transparent||m.transmission>0||o.morphTargetInfluences?.length||o.matrixWorld.determinant()<0)return;
  const attrs=Object.entries(g.attributes).map(([k,v])=>`${k}:${v.itemSize}:${v.normalized}`).sort().join('|');
  const key=m.uuid+'|'+attrs;
  if(!groups.has(key))groups.set(key,[]);groups.get(key).push(o);
 });
 for(const meshes of groups.values()){
  if(meshes.length<2)continue;
  const geometries=meshes.map(o=>{const g=o.geometry.index?o.geometry.toNonIndexed():o.geometry.clone();g.applyMatrix4(inverse.clone().multiply(o.matrixWorld));return g;});
  const combined=mergeGeometries(geometries,false);geometries.forEach(g=>g.dispose());
  if(!combined)continue;
  combined.computeBoundingBox();combined.computeBoundingSphere();
  const batch=new THREE.Mesh(combined,meshes[0].material);batch.name='Static review batch';
  for(const o of meshes){originalGeometries.add(o.geometry);o.removeFromParent();}
  root.add(batch);
 }
 const retained=new Set();let after=0;
 root.traverse(o=>{if(o.isMesh){after++;retained.add(o.geometry);}});
 for(const g of originalGeometries)if(!retained.has(g))g.dispose();
 return {before,after};
}

