# Removed 2026-10-18: Emerald AI

Vendor files nothing uses: not the build scenes, not any scene, zone or asset in `Assets/_Game`, not Resources, settings or plugins. Three checks agreed (Maintainer, Unity's dependencies, written references) and none is loaded by path or name.

324 files, 104.0 MB. Last commit that still has them: `ff91e25aa8c2`.

To bring one back: Unity ▸ **Krytical Nova ▸ Project ▸ Restore Assets**, *Removed* tab, search its name, **Restore** (it comes back with the materials, textures and meshes it needs, under its old path and GUID). Without the tool: `git checkout ff91e25aa8c2 -- "<path>" "<path>.meta"`.

## Emerald AI (324 files, 104.0 MB)

Get it back from: Emerald AI 2026.unitypackage

**Assets/_ThirdParty/Emerald AI/Combat Actions/**

- Block Action.asset · Asset · 942 B
- Dodge Action.asset · Asset · 925 B
- Random Movement Action.asset · Asset · 948 B
- Strafe Action.asset · Asset · 788 B
- Switch Target Action.asset · Asset · 786 B

**Assets/_ThirdParty/Emerald AI/Demo/Demo Scenes/**

- Alignment Demo.unity · Scene · 191 KB
- Companion Demo.unity · Scene · 489 KB
- Cover Demo.unity · Scene · 801 KB
- Debugging Demo.unity · Scene · 203 KB
- Dynamic Wandering Demo.unity · Scene · 204 KB
- Equippable Weapon Demo.unity · Scene · 353 KB
- Explosion Damage Demo.unity · Scene · 464 KB
- Fleeing Demo.unity · Scene · 182 KB
- Footsteps Demo.unity · Scene · 192 KB
- Grenade Demo.unity · Scene · 353 KB
- Healing Demo.unity · Scene · 360 KB
- Health Bar Demo.unity · Scene · 357 KB
- Inverse Kinematics Demo.unity · Scene · 213 KB
- Knockback Demo.unity · Scene · 350 KB
- Melee Combat Demo.unity · Scene · 350 KB
- Mouse Movement Demo.unity · Scene · 170 KB
- NavMesh Link Demo.unity · Scene · 258 KB
- Optimization Demo.unity · Scene · 186 KB
- Patrolling Demo.unity · Scene · 326 KB
- Player Damage Demo.unity · Scene · 365 KB
- Ragdoll Demo.unity · Scene · 175 KB
- Ranged Combat Demo.unity · Scene · 213 KB
- Shooter AI Demo.unity · Scene · 783 KB
- Slow Effect Demo.unity · Scene · 464 KB
- Sound Detection Demo.unity · Scene · 183 KB
- Summoning AI Demo.unity · Scene · 361 KB
- Waypoints Demo.unity · Scene · 184 KB
- Working Villager Demo.unity · Scene · 197 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animation Profiles/**

- Basic Example AI.asset · Asset · 8 KB
- Cover Example AI.asset · Asset · 11 KB
- Equippable Sword Example AI.asset · Asset · 11 KB
- Example Alignment AI.asset · Asset · 11 KB
- Fists Example AI.asset · Asset · 12 KB
- Gatherer Example AI.asset · Asset · 8 KB
- Healing Example AI.asset · Asset · 11 KB
- Shooter Example AI.asset · Asset · 11 KB
- Sorcerer Example AI.asset · Asset · 9 KB
- Summoning Example AI.asset · Asset · 11 KB
- Sword Example AI.asset · Asset · 12 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Ellen/**

- @EllenGunHit.fbx · Model · 1.9 MB
- @EllenGunIdle 2.fbx · Model · 1.9 MB
- @EllenGunIdle.fbx · Model · 1.7 MB
- @EllenGunPutAway.fbx · Model · 2.0 MB
- @EllenGunShoot.fbx · Model · 1.9 MB
- @EllenGunTakeOut.fbx · Model · 2.0 MB
- @EllenTurnOnSpotLeft45.fbx · Model · 1.9 MB
- @EllenTurnOnSpotLeft90.fbx · Model · 1.9 MB
- @EllenTurnOnSpotRight45.fbx · Model · 1.9 MB
- @EllenTurnOnSpotRight90.fbx · Model · 1.9 MB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Ellen/Materials/**

- Ellen_Body_Dirty_Mat.mat · Material · 2 KB
- Ellen_EyeLash_Mat.mat · Material · 2 KB
- Ellen_Head_Dirty_Mat.mat · Material · 3 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/IK Demo/**

- Look At Point Example.anim · Animation · 10 KB
- Sphere.controller · Animator · 1 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/RPG Character/Animations/2Hand-Sword/**

- RPG-Character@2Hand-Sword-Attack1.FBX · Model · 514 KB
- RPG-Character@2Hand-Sword-Attack10.FBX · Model · 514 KB
- RPG-Character@2Hand-Sword-Attack11.FBX · Model · 514 KB
- RPG-Character@2Hand-Sword-Attack2.FBX · Model · 514 KB
- RPG-Character@2Hand-Sword-Attack3.FBX · Model · 514 KB
- RPG-Character@2Hand-Sword-Attack4.FBX · Model · 514 KB
- RPG-Character@2Hand-Sword-Attack5.FBX · Model · 514 KB
- RPG-Character@2Hand-Sword-Attack6.FBX · Model · 514 KB
- RPG-Character@2Hand-Sword-Attack7.FBX · Model · 514 KB
- RPG-Character@2Hand-Sword-Attack8.FBX · Model · 514 KB
- RPG-Character@2Hand-Sword-Attack9.FBX · Model · 514 KB
- RPG-Character@2Hand-Sword-DiveRoll-Forward.FBX · Model · 536 KB
- RPG-Character@2Hand-Sword-DiveRoll-Right.FBX · Model · 536 KB
- RPG-Character@2Hand-Sword-Fall.FBX · Model · 492 KB
- RPG-Character@2Hand-Sword-GetHit-B1.FBX · Model · 425 KB
- RPG-Character@2Hand-Sword-GetHit-F1.FBX · Model · 425 KB
- RPG-Character@2Hand-Sword-GetHit-F2.FBX · Model · 425 KB
- RPG-Character@2Hand-Sword-GetHit-L1.FBX · Model · 564 KB
- RPG-Character@2Hand-Sword-GetHit-R1.FBX · Model · 564 KB
- RPG-Character@2Hand-Sword-Getup1.FBX · Model · 983 KB
- RPG-Character@2Hand-Sword-Idle-Static.FBX · Model · 648 KB
- RPG-Character@2Hand-Sword-Idle.FBX · Model · 648 KB
- RPG-Character@2Hand-Sword-Jump-Flip.FBX · Model · 548 KB
- RPG-Character@2Hand-Sword-Jump.FBX · Model · 503 KB
- RPG-Character@2Hand-Sword-Knockback-Back1.FBX · Model · 514 KB
- RPG-Character@2Hand-Sword-Knockback-Back2.FBX · Model · 458 KB
- RPG-Character@2Hand-Sword-Knockdown1.FBX · Model · 570 KB
- RPG-Character@2Hand-Sword-Land.FBX · Model · 503 KB
- RPG-Character@2Hand-Sword-Run-Backward-Left.FBX · Model · 447 KB
- RPG-Character@2Hand-Sword-Run-Backward-Right.FBX · Model · 447 KB
- RPG-Character@2Hand-Sword-Run-Backward.FBX · Model · 447 KB
- RPG-Character@2Hand-Sword-Run-Forward-Attack1.FBX · Model · 447 KB
- RPG-Character@2Hand-Sword-Run-Forward-Left.FBX · Model · 447 KB
- RPG-Character@2Hand-Sword-Run-Forward-Right.FBX · Model · 447 KB
- RPG-Character@2Hand-Sword-Run-Forward.FBX · Model · 447 KB
- RPG-Character@2Hand-Sword-Run-Left.FBX · Model · 447 KB
- RPG-Character@2Hand-Sword-Run-Right.FBX · Model · 447 KB
- RPG-Character@2Hand-Sword-Sheath-Back-Unarmed.FBX · Model · 480 KB
- RPG-Character@2Hand-Sword-Strafe-Backward-Left.FBX · Model · 447 KB
- RPG-Character@2Hand-Sword-Strafe-Backward-Right.FBX · Model · 447 KB
- RPG-Character@2Hand-Sword-Strafe-Backward.FBX · Model · 447 KB
- RPG-Character@2Hand-Sword-Strafe-Forward-Left.FBX · Model · 447 KB
- RPG-Character@2Hand-Sword-Strafe-Forward-Right.FBX · Model · 447 KB
- RPG-Character@2Hand-Sword-Strafe-Forward.FBX · Model · 447 KB
- RPG-Character@2Hand-Sword-Strafe-Left.FBX · Model · 447 KB
- RPG-Character@2Hand-Sword-Strafe-Right.FBX · Model · 447 KB
- RPG-Character@2Hand-Sword-Turn.FBX · Model · 648 KB
- RPG-Character@2Hand-Sword-Unsheath-Back-Unarmed.FBX · Model · 480 KB
- RPG-Character@2Hand-Sword-Walk-Slow.FBX · Model · 581 KB
- RPG-Character@2Hand-Sword-Walk.FBX · Model · 447 KB
- SwordTurnLeft.anim · Animation · 1.6 MB
- SwordTurnRight.anim · Animation · 1.6 MB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/RPG Character/Avatars/**

- RPG-Character-Avatar.ht · Other · 1 KB
- RPG-Character-Upperbody-AvatarMask.mask · Avatar mask · 6 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/RPG Character/Materials/**

- RPG-Character (Blue).mat · Material · 2 KB
- RPG-Character (Companion).mat · Material · 2 KB
- RPG-Character (Red).mat · Material · 2 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/RPG Character/Models/Characters/**

- RPG-Character-Bones.FBX · Model · 258 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/RPG Character/Models/Weapons/**

- 2Hand-Sword.FBX · Model · 34 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/RPG Character/Prefabs/**

- RPG-Character (Ragdoll).prefab · Prefab · 109 KB
- RPG-Character-NPC.prefab · Prefab · 56 KB
- RPG-Character.prefab · Prefab · 59 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/RPG Character/Textures/**

- RPG-CharacterBlue.psd · Texture · 27 KB
- RPG-CharacterCompanion.psd · Texture · 14 KB
- RPG-CharacterRed.psd · Texture · 14 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Sorceress/Animation Controller/**

- Sorceress Warrior Animation Controller.controller · Animator · 16 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Sorceress/Animations/**

- Sorceress@Attack2.FBX · Model · 526 KB
- Sorceress@Fall.FBX · Model · 472 KB
- Sorceress@Idle.FBX · Model · 636 KB
- Sorceress@Jump.FBX · Model · 581 KB
- Sorceress@Run.FBX · Model · 636 KB
- Sorceress@Stunned.FBX · Model · 636 KB
- Sorceress@Walk.FBX · Model · 636 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Sorceress/Characters/**

- Sorceress-Hair.FBX · Model · 36 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Sorceress/Materials/**

- Sorceress.mat · Material · 1 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Sorceress/Textures/**

- Sorceress.psd · Texture · 23 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Villager and Basic/Basic Motions/Animations/Idles/**

- BasicMotions@Idle01.fbx · Model · 2.0 MB
- BasicMotions@Idle02.fbx · Model · 1.9 MB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Villager and Basic/Basic Motions/Animations/Movement/**

- BasicMotions@Fall01.fbx · Model · 508 KB
- BasicMotions@Jump01.fbx · Model · 1.5 MB
- BasicMotions@Run01.fbx · Model · 2.2 MB
- BasicMotions@Sprint01.fbx · Model · 1.4 MB
- BasicMotions@Strafe01.fbx · Model · 909 KB
- BasicMotions@Walk01.fbx · Model · 3.0 MB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Villager and Basic/Basic Motions/Animations/Social/Conversation/**

- BasicMotions@Talk01.fbx · Model · 928 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Villager and Basic/Basic Motions/Animations/Turn/**

- @EllenTurnOnSpotLeft90.fbx · Model · 1.9 MB
- @EllenTurnOnSpotRight90.fbx · Model · 1.9 MB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Villager and Basic/Basic Motions/Materials/**

- BasicMotionsFloor.mat · Material · 2 KB
- BasicMotionsMaterial.mat · Material · 2 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Villager and Basic/Basic Motions/Models/**

- BasicMotionsDummyModel.fbx · Model · 685 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Villager and Basic/Villager Animations/Animations/**

- Villager@Farm-Working01.fbx · Model · 1.0 MB
- Villager@Fishing01.fbx · Model · 900 KB
- Villager@Fishing02.fbx · Model · 2.0 MB
- Villager@Gathering01.fbx · Model · 757 KB
- Villager@Hammer-Working01.fbx · Model · 1.4 MB
- Villager@Idle01.fbx · Model · 3.6 MB
- Villager@Pickaxe-Mining01.fbx · Model · 1.0 MB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Villager and Basic/Villager Animations/AnimatorControllers/**

- VillagerFREE@Farming.controller · Animator · 1 KB
- VillagerFREE@Fishing.controller · Animator · 3 KB
- VillagerFREE@Gathering.controller · Animator · 1 KB
- VillagerFREE@HammerWorking.controller · Animator · 1 KB
- VillagerFREE@Idle.controller · Animator · 1 KB
- VillagerFREE@Mining.controller · Animator · 1 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Villager and Basic/Villager Animations/Materials/**

- VillagerMaterial.mat · Material · 2 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Villager and Basic/Villager Animations/Models/Characters/**

- VillagerHuman.fbx · Model · 932 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Villager and Basic/Villager Animations/Models/Other/**

- GoldOre.fbx · Model · 46 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Villager and Basic/Villager Animations/Models/Tools/**

- VillagerFishingPole.fbx · Model · 25 KB
- VillagerHammer.fbx · Model · 25 KB
- VillagerPickaxe.fbx · Model · 33 KB
- VillagerPlow.fbx · Model · 28 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Villager and Basic/Villager Animations/Prefabs/**

- VillagerPickaxe.prefab · Prefab · 2 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Villager and Basic/Villager Animations/Prefabs/Tools/**

- VillagerFishingRod.prefab · Prefab · 2 KB
- VillagerHammer.prefab · Prefab · 2 KB
- VillagerPlow.prefab · Prefab · 2 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Villager and Basic/Villager Animations/Textures/**

- VillagerTexture.png · Texture · 7 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animator Controllers/**

- Cover Example AI.overrideController · Animator · 471 KB
- Equippable Sword Example AI.overrideController · Animator · 465 KB
- Example Alignment AI.overrideController · Animator · 465 KB
- Fist Example AI.overrideController · Animator · 481 KB
- Gatherer Example AI.overrideController · Animator · 463 KB
- Healing Example AI.overrideController · Animator · 465 KB
- IK Example AI.overrideController · Animator · 464 KB
- Shooter Example AI.overrideController · Animator · 465 KB
- Sorcerer Example AI.overrideController · Animator · 464 KB
- Summoning Example AI.overrideController · Animator · 465 KB
- Sword Example AI.overrideController · Animator · 481 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Demo Abilities/Ability Objects/**

- Arrow Barrage (Aerial).asset · Asset · 3 KB
- Bullet Projectile Ability.asset · Asset · 3 KB
- Grenade Ability.asset · Asset · 2 KB
- Heal Ally Ability.asset · Asset · 1 KB
- Heal Self Ability.asset · Asset · 1 KB
- Ice Shards (Slowed Effect).asset · Asset · 3 KB
- Ice Shards (Spread Projectile).asset · Asset · 3 KB
- Rock (Ground Projectile).asset · Asset · 3 KB
- Spike Barrage (Aerial).asset · Asset · 3 KB
- Summon Ability.asset · Asset · 1 KB
- Sword (Melee).asset · Asset · 2 KB
- Sword with Knockback (Melee).asset · Asset · 2 KB
- Unarmed (Melee).asset · Asset · 2 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Demo Abilities/Ability Source/Effects Objects/**

- Aerial Spawn Effect.prefab · Prefab · 113 KB
- Arrow Object.prefab · Prefab · 2 KB
- Arrow Type 1.prefab · Prefab · 3 KB
- Arrow Type 2.prefab · Prefab · 2 KB
- Bullet (Body).prefab · Prefab · 114 KB
- Bullet (Impact).prefab · Prefab · 114 KB
- Bullet (Sparks).prefab · Prefab · 114 KB
- Bullet Object.prefab · Prefab · 4 KB
- Grenade Explosion.prefab · Prefab · 114 KB
- Ground Ability.prefab · Prefab · 230 KB
- Healing Ability.prefab · Prefab · 114 KB
- Healing Burst.prefab · Prefab · 114 KB
- Ice Impact.prefab · Prefab · 114 KB
- Ice Shard Impact.prefab · Prefab · 114 KB
- Ice Shard.prefab · Prefab · 117 KB
- Rock Impact.prefab · Prefab · 114 KB
- Rock Projectile.prefab · Prefab · 116 KB
- Spike.prefab · Prefab · 4 KB
- Summon Spawn Effect.prefab · Prefab · 229 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Demo Abilities/Ability Source/Materials/**

- Bullet Body.mat · Material · 2 KB
- Bullet Impact.mat · Material · 2 KB
- Bullet Sparks.mat · Material · 2 KB
- Bullet Trail.mat · Material · 2 KB
- Heal Particle.mat · Material · 2 KB
- Summon Particle.mat · Material · 2 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Demo Abilities/Ability Source/Other/**

- Example Summoned AI 1.prefab · Prefab · 126 KB
- Example Summoned AI.prefab · Prefab · 136 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Example Reactions/Attract Modifier/**

- Box.asset · Asset · 1 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Example Reactions/Combat/**

- Aware Reaction (Combat).asset · Asset · 1 KB
- Suspicious Reaction (Combat).asset · Asset · 900 B
- Unaware Reaction (Combat).asset · Asset · 1 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Example Reactions/Emerald AI Wiki Examples/**

- Advanced Reaction.asset · Asset · 2 KB
- Complex Reaction.asset · Asset · 3 KB
- Intermediate Reaction.asset · Asset · 1 KB
- Simple Reaction.asset · Asset · 1 KB
- Very Simple Reaction.asset · Asset · 770 B

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Example Reactions/Flee/**

- Aware Reaction (Flee).asset · Asset · 647 B
- Suspicious Reaction (Flee).asset · Asset · 877 B
- Unaware Reaction (Flee).asset · Asset · 651 B

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Example Reactions/Sound Detection Demo Examples/**

- (Arrow) Attract Modifier.asset · Asset · 2 KB
- (Spear) Attract Modifier.asset · Asset · 2 KB
- (Stone) Attract Modifier.asset · Asset · 2 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Footstep Surface Objects/**

- Default Footstep Surface.asset · Asset · 1 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Footstep Surface Objects/Footstep Demo Source/**

- Footprint.mat · Material · 2 KB
- Footprint.png · Texture · 17 KB
- Footprint.prefab · Prefab · 2 KB
- Footstep Effect.prefab · Prefab · 114 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Models/Demo Models/Materials/**

- Arrow Material.mat · Material · 2 KB
- Footprint.mat · Material · 2 KB
- Grenade Material.mat · Material · 2 KB
- Player Material.mat · Material · 2 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Models/Demo Models/Mesh/**

- Arrow.asset · Asset · 55 KB
- Circular Platform.asset · Asset · 10 KB
- Footprint Mesh.asset · Asset · 3 KB
- Grenade.asset · Asset · 41 KB
- Medium Tower.asset · Asset · 5 KB
- Multi-Directional Platform.asset · Asset · 5 KB
- Platform Corner.asset · Asset · 21 KB
- Platform Skinny.asset · Asset · 5 KB
- Platform.asset · Asset · 6 KB
- Pole.asset · Asset · 10 KB
- Rock Piece.asset · Asset · 10 KB
- Small Tower.asset · Asset · 5 KB
- Spike.asset · Asset · 16 KB
- Tall Tower.asset · Asset · 5 KB
- Upper Platform.asset · Asset · 5 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Models/Demo Models/Mesh/Materials/**

- Pistol_Mat.mat · Material · 2 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Models/Demo Models/Prefabs/**

- Arrow.prefab · Prefab · 2 KB
- Footprint.prefab · Prefab · 2 KB
- Grenade.prefab · Prefab · 3 KB
- Ice Shard.prefab · Prefab · 2 KB
- Medium Tower.prefab · Prefab · 2 KB
- Multi-Directional Platform.prefab · Prefab · 2 KB
- Platform.prefab · Prefab · 2 KB
- Rock Piece.prefab · Prefab · 2 KB
- Small Tower.prefab · Prefab · 2 KB
- Spike.prefab · Prefab · 2 KB
- Tall Tower.prefab · Prefab · 2 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Models/Terrain/**

- Demo Terrain Layer.terrainlayer · Other · 738 B
- Demo Terrain.asset · Asset · 1.9 MB
- GridBox_Default.png · Texture · 9 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/NavMesh Data/Alignment Demo/**

- NavMesh.asset · Asset · 36 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/NavMesh Data/Combat Demo/**

- NavMesh.asset · Asset · 37 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/NavMesh Data/Cover Demo/**

- NavMesh.asset · Asset · 44 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/NavMesh Data/Debugging Demo/**

- NavMesh.asset · Asset · 36 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/NavMesh Data/Dynamic Wandering Demo/**

- NavMesh.asset · Asset · 36 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/NavMesh Data/Explosion Damage Demo/**

- NavMesh.asset · Asset · 27 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/NavMesh Data/Fleeing Demo/**

- NavMesh.asset · Asset · 28 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/NavMesh Data/Inverse Kinematics Demo/**

- NavMesh.asset · Asset · 30 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/NavMesh Data/Mouse Movement Demo/**

- NavMesh.asset · Asset · 26 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/NavMesh Data/NavMesh Link Demo/**

- NavMesh.asset · Asset · 30 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/NavMesh Data/Optimization Demo/**

- NavMesh.asset · Asset · 29 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/NavMesh Data/Patrolling Demo/**

- NavMesh.asset · Asset · 29 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/NavMesh Data/Ragdoll Demo/**

- NavMesh.asset · Asset · 29 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/NavMesh Data/Sound Detection Demo/**

- NavMesh.asset · Asset · 25 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/NavMesh Data/Waypoints Demo/**

- NavMesh.asset · Asset · 29 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/NavMesh Data/Working Villager Demo/**

- NavMesh.asset · Asset · 29 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Sound Profiles/**

- Example AI (Fists).asset · Asset · 1 KB
- Example AI (Shooter).asset · Asset · 1 KB
- Example AI (Sword).asset · Asset · 2 KB
- Example AI (Villager).asset · Asset · 1 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Sounds/Block/**

- Sword Block.wav · Audio · 219 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Sounds/Footsteps/**

- Footstep_Earth_Walk_01.wav · Audio · 67 KB
- Footstep_Earth_Walk_02.wav · Audio · 67 KB
- Footstep_Earth_Walk_03.wav · Audio · 67 KB
- Footstep_Earth_Walk_04.wav · Audio · 67 KB
- Footstep_Earth_Walk_05.wav · Audio · 67 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Sounds/Impacts/**

- Grenade Bounce.wav · Audio · 30 KB
- Pickaxe.wav · Audio · 144 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Sounds/Interaction/**

- Box Collision 1.wav · Audio · 82 KB
- Box Collision 2.wav · Audio · 56 KB
- Box Collision 3.wav · Audio · 57 KB
- Equippable Sword.wav · Audio · 124 KB
- Grenade Throw.wav · Audio · 73 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Sounds/Spell/**

- Fire Spell Impact.wav · Audio · 670 KB
- Fire Spell Spawn.wav · Audio · 511 KB
- Summon.wav · Audio · 251 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Sounds/Swings/**

- Fist Swing.wav · Audio · 28 KB
- Swing 1.wav · Audio · 141 KB
- Swing 2.wav · Audio · 141 KB
- Swing 3.wav · Audio · 136 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Sounds/Weapons/**

- EquipGun.wav · Audio · 17 KB
- HolsterGun.wav · Audio · 17 KB
- ShootGun.wav · Audio · 147 KB

**Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Textures/**

- Emerald AI 2024 Icon.png · Texture · 101 KB
- Emerald AI 2024 Text.png · Texture · 14 KB
