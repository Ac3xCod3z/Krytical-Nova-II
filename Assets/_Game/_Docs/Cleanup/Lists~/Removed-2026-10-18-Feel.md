# Removed 2026-10-18: Feel

Vendor files nothing uses: not the build scenes, not any scene, zone or asset in `Assets/_Game`, not Resources, settings or plugins. Three checks agreed (Maintainer, Unity's dependencies, written references) and none is loaded by path or name.

785 files, 149.7 MB. Last commit that still has them: `ff91e25aa8c2`.

To bring one back: Unity ▸ **Krytical Nova ▸ Project ▸ Restore Assets**, *Removed* tab, search its name, **Restore** (it comes back with the materials, textures and meshes it needs, under its old path and GUID). Without the tool: `git checkout ff91e25aa8c2 -- "<path>" "<path>.meta"`.

## Feel (785 files, 149.7 MB)

Get it back from: Feel.unitypackage

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/MMFeedbacksDemo/**

- MMF_PlayerDemo.unity · Scene · 10.2 MB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/MMFeedbacksDemo/Animations/**

- FeedbackDemoGhostAnimator.controller · Animator · 2 KB
- FeedbackGhostAnimation.anim · Animation · 8 KB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/MMFeedbacksDemo/Camera/**

- MMFeedbacksDemo Alt Camera Profile.asset · Asset · 26 KB
- MMFeedbacksDemo Filter Camera Profile.asset · Asset · 26 KB
- MMFeedbacksDemo Main Camera Profile.asset · Asset · 26 KB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/MMFeedbacksDemo/Lighting/**

- LightingData.asset · Asset · 7.3 MB
- ReflectionProbe-0.exr · Texture · 64 KB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/MMFeedbacksDemo/Materials/**

- FeedbacksDemoBackgroundCube.mat · Material · 5 KB
- FeedbacksDemoBurger.mat · Material · 4 KB
- FeedbacksDemoCheese.mat · Material · 4 KB
- FeedbacksDemoCupcakeMaterial.mat · Material · 5 KB
- FeedbacksDemoDudeBody.mat · Material · 5 KB
- FeedbacksDemoEyes.mat · Material · 4 KB
- FeedbacksDemoGreen.mat · Material · 5 KB
- FeedbacksDemoGround.mat · Material · 4 KB
- FeedbacksDemoGroundDust.mat · Material · 4 KB
- FeedbacksDemoGroundRocks.mat · Material · 4 KB
- FeedbacksDemoRed.mat · Material · 4 KB
- FeedbacksDemoSkybox1.mat · Material · 2 KB
- FeedbacksDemoSkybox2.mat · Material · 2 KB
- FeedbacksDemoSteak.mat · Material · 4 KB
- FeedbacksDemoTextMaterial.mat · Material · 2 KB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/MMFeedbacksDemo/Models/**

- MMApple.fbx · Model · 40 KB
- MMBurger.fbx · Model · 67 KB
- MMCupcake.fbx · Model · 42 KB
- Rock1.fbx · Model · 17 KB
- Rock2.fbx · Model · 16 KB
- Rock3.fbx · Model · 17 KB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/MMFeedbacksDemo/Models/Dude/**

- DudeAnimator.controller · Animator · 4 KB
- DudeEyes.anim · Animation · 4 KB
- DudeEyesAnimator.controller · Animator · 2 KB
- MMFeedbackDemoDude@Idle.fbx · Model · 407 KB
- MMFeedbackDemoDude@Jump.fbx · Model · 237 KB
- MMFeedbackDemoDude@TPose.fbx · Model · 265 KB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/MMFeedbacksDemo/Prefabs/**

- FeedbackDemoBackgroundCube.prefab · Prefab · 2 KB
- FeedbackDemoMiniSpheresFX.prefab · Prefab · 118 KB
- FeedbackDemoSparks.prefab · Prefab · 120 KB
- FeedbackDemoSphere.prefab · Prefab · 18 KB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/MMFeedbacksDemo/Sounds/**

- MMFeedbacksDemoJump.wav · Audio · 44 KB
- MMFeedbacksDemoKey.wav · Audio · 5 KB
- MMFeedbacksDemoSnap.wav · Audio · 21 KB
- MMFeedbacksDemoTrack.wav · Audio · 6.4 MB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/MMFeedbacksDemo/Textures/**

- FeedbacksDemoFaderMask.png · Texture · 15 KB
- FeedbacksDemoGhost.png · Texture · 1 KB
- MMFeedbacksDemoGround.png · Texture · 9 KB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/MMFeedbacksDemo/Textures/Paper/**

- PaperCol.png · Texture · 534 KB
- PaperDisp.png · Texture · 760 KB
- PaperNormal.png · Texture · 3.5 MB
- PaperRoughness.png · Texture · 753 KB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/MMFeedbacksDemo/TMP/**

- Lato-Black SDF MMFeedbacks Outline Demo.mat · Material · 2 KB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/MMFeedbacksDemo/UI/**

- Spacer.prefab · Prefab · 2 KB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/MMFeedbacksDemo/Volumes_URP/**

- MMFeedbacksDemoAltVolume.asset · Asset · 5 KB
- MMFeedbacksDemoFilterVolume.asset · Asset · 10 KB
- MMFeedbacksDemoMainVolume.asset · Asset · 5 KB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/SequencingDemo/**

- SequencerDemo.unity · Scene · 1.5 MB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/SequencingDemo/DemoSequences/**

- DrumSequence.asset · Asset · 8 KB
- FeedbacksSequence.asset · Asset · 2 KB
- RecordedSequence.asset · Asset · 1 KB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/SequencingDemo/Lighting/**

- SequencerDemoSettings.lighting · Other · 1 KB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/SequencingDemo/Materials/**

- CubeMaterial.mat · Material · 5 KB
- SequencerCarpet.mat · Material · 5 KB
- SequencerCarpet05.mat · Material · 5 KB
- SequencerCarpet06.mat · Material · 5 KB
- SequencerChip.mat · Material · 5 KB
- SequencerChristmasTree.mat · Material · 5 KB
- SequencerFabric06.mat · Material · 5 KB
- SequencerFabric09.mat · Material · 5 KB
- SequencerFacade.mat · Material · 5 KB
- SequencerMetal.mat · Material · 5 KB
- SequencerPaintedMetal.mat · Material · 5 KB
- SequencerTiles12.mat · Material · 5 KB
- SequencerTiles31.mat · Material · 5 KB
- SequencerTiles33.mat · Material · 5 KB
- SequencerTiles44.mat · Material · 5 KB
- SequencerTiles60.mat · Material · 5 KB
- SequencerTiles65.mat · Material · 5 KB
- SequencerTiles67.mat · Material · 5 KB
- SequencerTiles69.mat · Material · 5 KB
- SequencerWood.mat · Material · 5 KB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/SequencingDemo/SequencerDemo_Profiles/**

- MMSequencerDemoGlobalPPP.asset · Asset · 26 KB
- MMSequencerDemoGlobalPPP_URP.asset · Asset · 5 KB
- MMSequencerDemoMovingPPP.asset · Asset · 2 KB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/SequencingDemo/Sounds/**

- MMSequencingClick.wav · Audio · 9 KB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/SequencingDemo/Sounds/Kit/**

- MMSequencingBass1.wav · Audio · 436 KB
- MMSequencingBass2.wav · Audio · 129 KB
- MMSequencingBass3.wav · Audio · 71 KB
- MMSequencingCrash1.wav · Audio · 6 KB
- MMSequencingCrash2.wav · Audio · 18 KB
- MMSequencingCymbal1.wav · Audio · 119 KB
- MMSequencingCymbal2.wav · Audio · 293 KB
- MMSequencingCymbal3.wav · Audio · 90 KB
- MMSequencingHat1.wav · Audio · 39 KB
- MMSequencingHat2.wav · Audio · 16 KB
- MMSequencingNote1.wav · Audio · 70 KB
- MMSequencingNote2.wav · Audio · 32 KB
- MMSequencingNote3.wav · Audio · 52 KB
- MMSequencingTick.wav · Audio · 6 KB
- MMSequencingTom1.wav · Audio · 39 KB
- MMSequencingTom2.wav · Audio · 69 KB
- MMSequencingTom3.wav · Audio · 42 KB
- MMSequencingTom4.wav · Audio · 33 KB
- MMSequencingTom5.wav · Audio · 70 KB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/SequencingDemo/Textures/**

- Carpet05_AO.png · Texture · 215 KB
- Carpet05_col.png · Texture · 243 KB
- Carpet05_nrm.png · Texture · 240 KB
- Carpet05_rgh.png · Texture · 120 KB
- Carpet06_AO.png · Texture · 224 KB
- Carpet06_col.png · Texture · 242 KB
- Carpet06_nrm.png · Texture · 245 KB
- Carpet06_rgh.png · Texture · 107 KB
- Carpet10_AO.png · Texture · 364 KB
- Carpet10_col.png · Texture · 731 KB
- Carpet10_disp.png · Texture · 335 KB
- Carpet10_nrm.png · Texture · 768 KB
- Carpet10_rgh.png · Texture · 254 KB
- Chip03_col.png · Texture · 178 KB
- Chip03_met.png · Texture · 67 KB
- Chip03_nrm.png · Texture · 154 KB
- Chip03_rgh.png · Texture · 196 KB
- ChristmasTreeOrnament01_col.png · Texture · 178 KB
- ChristmasTreeOrnament01_disp.png · Texture · 198 KB
- ChristmasTreeOrnament01_met.png · Texture · 30 KB
- ChristmasTreeOrnament01_nrm.png · Texture · 162 KB
- ChristmasTreeOrnament01_rgh.png · Texture · 122 KB
- Fabric06_col.png · Texture · 533 KB
- Fabric06_disp.png · Texture · 338 KB
- Fabric06_mask.png · Texture · 372 KB
- Fabric06_nrm.png · Texture · 750 KB
- Fabric06_rgh.png · Texture · 301 KB
- Fabric09_AO.png · Texture · 67 KB
- Fabric09_col.png · Texture · 392 KB
- Fabric09_disp.png · Texture · 249 KB
- Fabric09_mask1.png · Texture · 95 KB
- Fabric09_mask2.png · Texture · 46 KB
- Fabric09_nrm.png · Texture · 514 KB
- Fabric09_rgh.png · Texture · 258 KB
- Facade02_col.png · Texture · 129 KB
- Facade02_emi.png · Texture · 29 KB
- Facade02_mask.png · Texture · 4 KB
- Facade02_met.png · Texture · 2 KB
- Facade02_nrm.png · Texture · 9 KB
- Metal03_col.png · Texture · 182 KB
- Metal03_met.png · Texture · 32 KB
- Metal03_nrm.png · Texture · 8 KB
- PaintedMetal02_col.png · Texture · 560 KB
- PaintedMetal02_mask.png · Texture · 286 KB
- PaintedMetal02_met.png · Texture · 176 KB
- PaintedMetal02_nrm.png · Texture · 471 KB
- PaintedMetal02_rgh.png · Texture · 217 KB
- Tiles12_col.png · Texture · 358 KB
- Tiles12_nrm.png · Texture · 271 KB
- Tiles12_rgh.png · Texture · 294 KB
- Tiles31_col.png · Texture · 177 KB
- Tiles31_nrm.png · Texture · 177 KB
- Tiles31_rgh.png · Texture · 196 KB
- Tiles33_col.png · Texture · 158 KB
- Tiles33_nrm.png · Texture · 231 KB
- Tiles33_rgh.png · Texture · 138 KB
- Tiles44_col.png · Texture · 595 KB
- Tiles44_nrm.png · Texture · 442 KB
- Tiles44_rgh.png · Texture · 248 KB
- Tiles60_col.png · Texture · 337 KB
- Tiles60_met.png · Texture · 5 KB
- Tiles60_nrm.png · Texture · 408 KB
- Tiles60_rgh.png · Texture · 274 KB
- Tiles65_AO.png · Texture · 111 KB
- Tiles65_col.png · Texture · 550 KB
- Tiles65_nrm.png · Texture · 641 KB
- Tiles65_rgh.png · Texture · 308 KB
- Tiles67_AO.png · Texture · 130 KB
- Tiles67_col.png · Texture · 443 KB
- Tiles67_nrm.png · Texture · 581 KB
- Tiles67_rgh.png · Texture · 288 KB
- Tiles69_AO.png · Texture · 27 KB
- Tiles69_col.png · Texture · 485 KB
- Tiles69_nrm.png · Texture · 621 KB
- Tiles69_rgh.png · Texture · 307 KB
- WoodFloor20_AO.png · Texture · 81 KB
- WoodFloor20_col.png · Texture · 421 KB
- WoodFloor20_nrm.png · Texture · 332 KB
- WoodFloor20_rgh.png · Texture · 247 KB

**Assets/_ThirdParty/Feel/MMFeedbacks/Demos/SequencingDemo/Textures/wip/**

- Rocks21_AO.png · Texture · 185 KB
- Rocks21_col.png · Texture · 229 KB
- Rocks21_nrm.png · Texture · 224 KB
- Rocks21_rgh.png · Texture · 212 KB

**Assets/_ThirdParty/Feel/MMFeedbacks/MMFeedbacks/Core/MMChannels/**

- ChannelA.asset · Asset · 411 B
- ChannelB.asset · Asset · 411 B
- ChannelC.asset · Asset · 411 B

**Assets/_ThirdParty/Feel/MMFeedbacks/MMFeedbacks/MMFloatingText/Prefabs/**

- MMFloatingText.prefab · Prefab · 17 KB
- MMFloatingTextPixel.prefab · Prefab · 19 KB

**Assets/_ThirdParty/Feel/MMFeedbacks/MMFeedbacksForThirdParty/TextMeshPro/MMFloatingText/Prefabs/**

- MMFloatingTextMeshPro.prefab · Prefab · 22 KB

**Assets/_ThirdParty/Feel/MMTools/Accessories/MMDebugMenu/Prefabs/**

- MMDebugMenu.prefab · Prefab · 37 KB
- MMDebugMenuButton.prefab · Prefab · 8 KB
- MMDebugMenuButtonBorder.prefab · Prefab · 8 KB
- MMDebugMenuCheckbox.prefab · Prefab · 12 KB
- MMDebugMenuChoicesThree.prefab · Prefab · 23 KB
- MMDebugMenuChoicesTwo.prefab · Prefab · 16 KB
- MMDebugMenuDebugPanel.prefab · Prefab · 35 KB
- MMDebugMenuSlider.prefab · Prefab · 16 KB
- MMDebugMenuSpacerBig.prefab · Prefab · 2 KB
- MMDebugMenuSpacerSmall.prefab · Prefab · 2 KB
- MMDebugMenuTab.prefab · Prefab · 7 KB
- MMDebugMenuTabContents.prefab · Prefab · 15 KB
- MMDebugMenuTabSpacer.prefab · Prefab · 1 KB
- MMDebugMenuTextLong.prefab · Prefab · 4 KB
- MMDebugMenuTextSmall.prefab · Prefab · 4 KB
- MMDebugMenuTextTiny.prefab · Prefab · 4 KB
- MMDebugMenuTitle.prefab · Prefab · 6 KB
- MMDebugMenuValue.prefab · Prefab · 8 KB
- MMDebugTouchDisplayPrefab.prefab · Prefab · 3 KB

**Assets/_ThirdParty/Feel/MMTools/Accessories/MMDebugMenu/Sprites/**

- MMDebugMenuSpritesheet.png · Texture · 21 KB

**Assets/_ThirdParty/Feel/MMTools/Accessories/MMGUI/Materials/**

- MMFaderRoundMaterialMask.mat · Material · 2 KB
- MMFaderRoundMaterialMasked.mat · Material · 2 KB

**Assets/_ThirdParty/Feel/MMTools/Accessories/MMGUI/Sprites/**

- MMFaderRoundMask.png · Texture · 18 KB
- MMTools_GUI_1x1.png · Texture · 909 B

**Assets/_ThirdParty/Feel/MMTools/Accessories/MMPrototypeTextures/MMBRP_Materials/**

- MMBPR_BlueCrosses.mat · Material · 4 KB
- MMBPR_BlueDots.mat · Material · 4 KB
- MMBPR_BlueFrame.mat · Material · 4 KB
- MMBPR_BlueSquares.mat · Material · 4 KB
- MMBPR_DarkGreyCrosses.mat · Material · 4 KB
- MMBPR_DarkGreyDots.mat · Material · 4 KB
- MMBPR_DarkGreyFrame.mat · Material · 4 KB
- MMBPR_DarkGreySquares.mat · Material · 4 KB
- MMBPR_GreenCrosses.mat · Material · 4 KB
- MMBPR_GreenDots.mat · Material · 4 KB
- MMBPR_GreenFrame.mat · Material · 4 KB
- MMBPR_GreenSquares.mat · Material · 4 KB
- MMBPR_LightGreyCrosses.mat · Material · 4 KB
- MMBPR_LightGreyDots.mat · Material · 4 KB
- MMBPR_LightGreyFrame.mat · Material · 4 KB
- MMBPR_LightGreySquares.mat · Material · 4 KB
- MMBPR_RedCrosses.mat · Material · 4 KB
- MMBPR_RedDots.mat · Material · 4 KB
- MMBPR_RedFrame.mat · Material · 4 KB
- MMBPR_RedSquares.mat · Material · 4 KB
- MMBPR_YellowCrosses.mat · Material · 4 KB
- MMBPR_YellowDots.mat · Material · 4 KB
- MMBPR_YellowFrame.mat · Material · 4 KB
- MMBPR_YellowSquares.mat · Material · 4 KB

**Assets/_ThirdParty/Feel/MMTools/Accessories/MMPrototypeTextures/MMBRP_Materials/Neutral/**

- MMBRP_NeutralBlack.mat · Material · 4 KB
- MMBRP_NeutralCheckers.mat · Material · 4 KB

**Assets/_ThirdParty/Feel/MMTools/Accessories/MMPrototypeTextures/MMBRP_Materials/Skybox/**

- MMBRP_BlueSkybox.mat · Material · 2 KB

**Assets/_ThirdParty/Feel/MMTools/Accessories/MMPrototypeTextures/Textures/MMCheckers/**

- MMCheckers.png · Texture · 7 KB

**Assets/_ThirdParty/Feel/MMTools/Accessories/MMPrototypeTextures/Textures/MMPlastic/**

- MMPlasticAO.png · Texture · 2.6 MB
- MMPlasticMetal.png · Texture · 12 KB
- MMPlasticNormal.png · Texture · 4.4 MB

**Assets/_ThirdParty/Feel/MMTools/Accessories/MMPrototypeTextures/Textures/MMProtoTextures/**

- MMProtoTextures_BlueCrosses.png · Texture · 14 KB
- MMProtoTextures_BlueDots.png · Texture · 15 KB
- MMProtoTextures_BlueFrame.png · Texture · 13 KB
- MMProtoTextures_BlueSquares.png · Texture · 14 KB
- MMProtoTextures_DarkGreyCrosses.png · Texture · 14 KB
- MMProtoTextures_DarkGreyDots.png · Texture · 15 KB
- MMProtoTextures_DarkGreyFrame.png · Texture · 13 KB
- MMProtoTextures_DarkGreySquares.png · Texture · 13 KB
- MMProtoTextures_GreenCrosses.png · Texture · 14 KB
- MMProtoTextures_GreenDots.png · Texture · 15 KB
- MMProtoTextures_GreenFrame.png · Texture · 12 KB
- MMProtoTextures_GreenSquares.png · Texture · 13 KB
- MMProtoTextures_LightGreyCrosses.png · Texture · 13 KB
- MMProtoTextures_LightGreyDots.png · Texture · 15 KB
- MMProtoTextures_LightGreyFrame.png · Texture · 12 KB
- MMProtoTextures_LightGreySquares.png · Texture · 13 KB
- MMProtoTextures_RedCrosses.png · Texture · 13 KB
- MMProtoTextures_RedDots.png · Texture · 14 KB
- MMProtoTextures_RedFrame.png · Texture · 11 KB
- MMProtoTextures_RedSquares.png · Texture · 12 KB
- MMProtoTextures_YellowCrosses.png · Texture · 12 KB
- MMProtoTextures_YellowDots.png · Texture · 14 KB
- MMProtoTextures_YellowFrame.png · Texture · 11 KB
- MMProtoTextures_YellowSquares.png · Texture · 12 KB

**Assets/_ThirdParty/Feel/MMTools/Accessories/MMVFX/MMBloomDirt/**

- MMBloomDirt1.png · Texture · 4.8 MB
- MMBloomDirt2.png · Texture · 8.1 MB
- MMBloomDirt3.png · Texture · 3.0 MB
- MMBloomDirt4.png · Texture · 5.0 MB

**Assets/_ThirdParty/Feel/MMTools/Accessories/MMVFX/MMBrushes/**

- MMBrush0.png · Texture · 70 KB
- MMBrush1.png · Texture · 181 KB
- MMBrush2.png · Texture · 223 KB
- MMBrush3.png · Texture · 81 KB
- MMBrush4.png · Texture · 100 KB
- MMBrush5.png · Texture · 112 KB

**Assets/_ThirdParty/Feel/MMTools/Accessories/MMVFX/MMNoise/**

- MMBayerNoise.png · Texture · 3 KB
- MMBlueNoise.png · Texture · 204 KB
- MMBrushNoise.png · Texture · 137 KB
- MMCellNoise.png · Texture · 662 KB
- MMCloudsNoise.png · Texture · 149 KB
- MMFireDirectionalAltNoise.png · Texture · 925 KB
- MMFireDirectionalNoise.png · Texture · 705 KB
- MMFireNoise.png · Texture · 544 KB
- MMFlowNoise.png · Texture · 196 KB
- MMPerlinNoise.png · Texture · 121 KB
- MMSimplexNoise.png · Texture · 81 KB
- MMVoronoiNoise.png · Texture · 271 KB
- MMWhiteNoise.png · Texture · 246 KB

**Assets/_ThirdParty/Feel/MMTools/Accessories/MMVFX/MMPalette/**

- MMLowPolyPalette.png · Texture · 65 KB

**Assets/_ThirdParty/Feel/MMTools/Accessories/MMVFX/MMParticles/**

- MMParticlesBlackSun.png · Texture · 53 KB
- MMParticlesBolt.png · Texture · 24 KB
- MMParticlesDust.png · Texture · 10 KB
- MMParticlesEclipse.png · Texture · 37 KB
- MMParticlesExplosion.png · Texture · 61 KB
- MMParticlesFlare.png · Texture · 4 KB
- MMParticlesFlash.png · Texture · 11 KB
- MMParticlesGamma.png · Texture · 23 KB
- MMParticlesHit.png · Texture · 10 KB
- MMParticlesJab.png · Texture · 10 KB
- MMParticlesLight.png · Texture · 24 KB
- MMParticlesSlash.png · Texture · 12 KB
- MMParticlesSmoke.png · Texture · 56 KB
- MMParticlesStar.png · Texture · 11 KB
- MMParticlesStorm.png · Texture · 87 KB
- MMParticlesWhirlwind.png · Texture · 94 KB

**Assets/_ThirdParty/Feel/MMTools/Accessories/MMVFX/MMRamps/**

- MMRamp0.png · Texture · 1 KB
- MMRamp1.png · Texture · 2 KB
- MMRamp2.png · Texture · 3 KB
- MMRamp3.png · Texture · 1 KB
- MMRamp4.png · Texture · 1 KB
- MMRamp5.png · Texture · 1 KB
- MMRamp6.png · Texture · 2 KB
- MMRamp7.png · Texture · 1 KB

**Assets/_ThirdParty/Feel/MMTools/Accessories/MMVFX/MMRipple/**

- MMRipple.prefab · Prefab · 66 KB
- MMRippleBubble1.mat · Material · 2 KB
- MMRippleBubble1NM.png · Texture · 109 KB
- MMRippleCircle1.mat · Material · 2 KB
- MMRippleCircle1NM.png · Texture · 102 KB
- MMRippleCircle2.mat · Material · 2 KB
- MMRippleCircle2NM.png · Texture · 131 KB
- MMRippleCircle3.mat · Material · 2 KB
- MMRippleCircle3NM.png · Texture · 134 KB
- MMRippleRosace1.mat · Material · 2 KB
- MMRippleRosace1NM.png · Texture · 486 KB
- MMRippleSaw1.mat · Material · 2 KB
- MMRippleSaw1NM.png · Texture · 106 KB
- MMRippleSaw2.mat · Material · 2 KB
- MMRippleSaw2NM.png · Texture · 96 KB
- MMRippleSpiral1.mat · Material · 2 KB
- MMRippleSpiral1NM.png · Texture · 101 KB
- MMRippleSpiral2.mat · Material · 2 KB
- MMRippleSpiral2NM.png · Texture · 112 KB
- MMRippleSquare1.mat · Material · 2 KB
- MMRippleSquare1NM.png · Texture · 73 KB
- MMRippleWaves1.mat · Material · 2 KB
- MMRippleWaves1NM.png · Texture · 166 KB

**Assets/_ThirdParty/Feel/MMTools/Accessories/MMVision/**

- ConeOfVisionAlpha.png · Texture · 42 KB

**Assets/_ThirdParty/Feel/MMTools/Core/MMAudio/MMSoundManager/Settings/**

- MMSoundManagerSettings.asset · Asset · 1 KB

**Assets/_ThirdParty/Feel/MMTools/Core/MMSaveLoad/**

- MMSaveLoadTestScene.unity · Scene · 43 KB

**Assets/_ThirdParty/Feel/MMTools/Core/MMSceneLoading/LoadingScreens/**

- LoadingScreen.unity · Scene · 26 KB
- MMAdditiveLoadingScreen.unity · Scene · 34 KB
- MMAdditiveLoadingScreenBigText.unity · Scene · 32 KB
- MMAdditiveLoadingScreenFastFade.unity · Scene · 17 KB
- MMAdditiveLoadingScreenSimple.unity · Scene · 32 KB

**Assets/_ThirdParty/Feel/MMTools/Core/MMSceneLoading/Sprites/**

- LoadingAnimation.anim · Animation · 18 KB
- LoadingAnimation.controller · Animator · 1 KB
- LoadingAnimation.png · Texture · 25 KB
- LoadingAnimationComplete.anim · Animation · 3 KB
- LoadingAnimationCompleteAnimator.controller · Animator · 1 KB
- LoadingCompleteAnimation.png · Texture · 9 KB
- LoadingProgressBar.png · Texture · 525 B
- MoreMountainsLogo.png · Texture · 11 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMBezierLineRenderer/**

- MMBezierLineRenderer.unity · Scene · 157 KB
- MMBezierLineRendererDemoDude.png · Texture · 18 KB
- MMBezierLineRendererDemoDudeHand.png · Texture · 2 KB
- MMBezierLineRendererSkybox.mat · Material · 2 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMControls/**

- MMControlsDemo.unity · Scene · 111 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMControls/MMControlsDemo/**

- LightingData.asset · Asset · 17 KB
- ReflectionProbe-0.exr · Texture · 135 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMControls/Sprites/**

- MMControlsDemoSpritesheet.png · Texture · 103 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMDebugMenu/**

- MMDebugMenu.unity · Scene · 308 KB
- MMDebugMenuDemoData.asset · Asset · 25 KB
- MMDebugMenuSettings.lighting · Other · 1 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMFollowTarget/**

- MMFollowTargetDemo.unity · Scene · 89 KB
- MMFollowTargetDemoGroundMaterial.mat · Material · 4 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMGhostCamera/**

- MMGhostCameraDemo.unity · Scene · 516 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMGhostCamera/Lighting/**

- MMGhostCameraDemoSettings.lighting · Other · 1 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMGhostCamera/Materials/**

- MMGhostCameraBuildings.mat · Material · 3 KB
- MMGhostCameraDark.mat · Material · 3 KB
- MMGhostCameraGround.mat · Material · 3 KB
- MMGhostCameraSkybox.mat · Material · 2 KB
- MMGhostCameraSpheres.mat · Material · 3 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMGhostCamera/Models/**

- MMGhostCameraCity.fbx · Model · 2.2 MB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMGhostCamera/PostProcessing/**

- MMGhostCameraPostProcessingProfile.asset · Asset · 4 KB
- MMGhostCameraPostProcessingProfile_URP.asset · Asset · 5 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMGizmos/**

- MMGizmoDemo.unity · Scene · 83 KB
- MMGizmoDemoCubeMaterial.mat · Material · 3 KB
- MMGizmoDemoSkybox.mat · Material · 2 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMGyroscope/**

- MMGyroscope.unity · Scene · 51 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMGyroscope/PostProcessing/**

- MMGyroscopePPProfile_URP.asset · Asset · 3 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMObservable/**

- MMObservableDemo.unity · Scene · 29 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMOrbitalCamera/**

- MMOrbitalCamera.unity · Scene · 46 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMOrbitalCamera/PostProcessing/**

- MMOrbitalCameraPPProfile_URP.asset · Asset · 3 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMPrototypeTextures/**

- MMPrototypeTexturesDemo.unity · Scene · 439 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMPrototypeTextures/MMPrototypeTexturesDemo/**

- LightingData.asset · Asset · 6.5 MB
- ReflectionProbe-0.exr · Texture · 111 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMPrototypeTextures/PostProcessing/**

- MMPrototypeTexturesPPProfile.asset · Asset · 4 KB
- MMPrototypeTexturesPPProfile_URP.asset · Asset · 4 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMRadio/**

- MMRadioDemo.unity · Scene · 674 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMSceneLoading/**

- LoadingTester.prefab · Prefab · 11 KB
- MMSceneLoadingDemoSceneA.unity · Scene · 34 KB
- MMSceneLoadingDemoSceneB.unity · Scene · 33 KB
- SceneStartsTester.prefab · Prefab · 5 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMTween/**

- MMTweenPlotter.unity · Scene · 46 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMTween/Camera/**

- MMPlotterPostProcessingProfile.asset · Asset · 2 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMTween/Fonts/Lato/SDF/**

- Lato SDF.asset · Asset · 568 KB
- Lato-BlackItalic SDF.asset · Asset · 567 KB
- Lato-Bold SDF.asset · Asset · 570 KB
- Lato-BoldItalic SDF.asset · Asset · 567 KB
- Lato-Hairline SDF.asset · Asset · 568 KB
- Lato-HairlineItalic SDF.asset · Asset · 567 KB
- Lato-Heavy SDF.asset · Asset · 571 KB
- Lato-HeavyItalic SDF.asset · Asset · 567 KB
- Lato-Italic SDF.asset · Asset · 570 KB
- Lato-Light SDF.asset · Asset · 567 KB
- Lato-LightItalic SDF.asset · Asset · 568 KB
- Lato-Medium SDF.asset · Asset · 567 KB
- Lato-MediumItalic SDF.asset · Asset · 567 KB
- Lato-Semibold SDF.asset · Asset · 567 KB
- Lato-SemiboldItalic SDF.asset · Asset · 567 KB
- Lato-Thin SDF.asset · Asset · 567 KB
- Lato-ThinItalic SDF.asset · Asset · 567 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMTween/Fonts/Lato/TTF/**

- Lato-BlackItalic.ttf · Font · 656 KB
- Lato-Bold.ttf · Font · 641 KB
- Lato-BoldItalic.ttf · Font · 682 KB
- Lato-Hairline.ttf · Font · 571 KB
- Lato-HairlineItalic.ttf · Font · 595 KB
- Lato-Heavy.ttf · Font · 649 KB
- Lato-HeavyItalic.ttf · Font · 682 KB
- Lato-Italic.ttf · Font · 706 KB
- Lato-Light.ttf · Font · 629 KB
- Lato-LightItalic.ttf · Font · 642 KB
- Lato-Medium.ttf · Font · 622 KB
- Lato-MediumItalic.ttf · Font · 679 KB
- Lato-SemiboldItalic.ttf · Font · 681 KB
- Lato-Thin.ttf · Font · 612 KB
- Lato-ThinItalic.ttf · Font · 638 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMTween/Materials/**

- MMPlotterCurvePointMaterial.mat · Material · 4 KB
- MMPlotterLineMaterial.mat · Material · 2 KB
- MMPlotterPointMaterial 1.mat · Material · 2 KB
- MMPlotterPointMaterial 10.mat · Material · 2 KB
- MMPlotterPointMaterial 11.mat · Material · 2 KB
- MMPlotterPointMaterial 2.mat · Material · 2 KB
- MMPlotterPointMaterial 3.mat · Material · 2 KB
- MMPlotterPointMaterial 4.mat · Material · 2 KB
- MMPlotterPointMaterial 5.mat · Material · 2 KB
- MMPlotterPointMaterial 6.mat · Material · 2 KB
- MMPlotterPointMaterial 7.mat · Material · 2 KB
- MMPlotterPointMaterial 8.mat · Material · 2 KB
- MMPlotterPointMaterial 9.mat · Material · 2 KB
- MMPlotterPointMaterial.mat · Material · 2 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMTween/Prefabs/**

- Plotter.prefab · Prefab · 11 KB
- PlotterAxisCanvas.prefab · Prefab · 25 KB
- PlotterPoint.prefab · Prefab · 2 KB

**Assets/_ThirdParty/Feel/MMTools/Demos/MMTween/Textures/**

- PlotterAxis.png · Texture · 23 KB

**Assets/_ThirdParty/Feel/MMTools/Foundation/MMAchievements/Prefabs/**

- AchievementDisplay.prefab · Prefab · 23 KB

**Assets/_ThirdParty/Feel/NiceVibrations/Demo/**

- NiceVibrationsDemo.unity · Scene · 1.5 MB

**Assets/_ThirdParty/Feel/NiceVibrations/Demo/Common/Fonts/**

- BIG JOHN.otf · Font · 10 KB
- RobotoMono-Bold.ttf · Font · 106 KB
- RobotoMono-Light.ttf · Font · 110 KB
- RobotoMono-Medium.ttf · Font · 106 KB
- RobotoMono-Regular.ttf · Font · 106 KB

**Assets/_ThirdParty/Feel/NiceVibrations/Demo/Common/Prefabs/**

- HorizontalSliderComponent.prefab · Prefab · 10 KB
- PaginationDot.prefab · Prefab · 2 KB
- VerticalProgressBar.prefab · Prefab · 8 KB
- VerticalSliderComponent.prefab · Prefab · 14 KB

**Assets/_ThirdParty/Feel/NiceVibrations/Demo/Common/Shaders/**

- CurveMaterial.mat · Material · 2 KB
- MaskedUI.mat · Material · 2 KB

**Assets/_ThirdParty/Feel/NiceVibrations/Demo/Common/Sounds/**

- NiceVibrationsContinuous.wav · Audio · 12 KB
- NiceVibrationsEmphasis.wav · Audio · 4 KB

**Assets/_ThirdParty/Feel/NiceVibrations/Demo/Common/Sprites/**

- NV1x1White.png · Texture · 927 B
- NV3Cols.png · Texture · 1 KB
- NV3Dots.png · Texture · 1 KB
- NV7Dots.png · Texture · 1 KB
- NVArrow.png · Texture · 1 KB
- NVDottedPattern.png · Texture · 1 KB
- NVKnob.png · Texture · 9 KB
- NVLogoNice.png · Texture · 3 KB
- NVLogoVibrations.png · Texture · 10 KB
- NVPaginationDot.png · Texture · 1 KB
- NVRoundedCorners.png · Texture · 1 KB
- NVRoundedCornersBig.png · Texture · 1 KB
- NVSwitchBackground.png · Texture · 1 KB
- NVSwitchIndicator.png · Texture · 1 KB
- NVSwitchKnob.png · Texture · 1 KB
- NVVerticalProgressBar.png · Texture · 1 KB

**Assets/_ThirdParty/Feel/NiceVibrations/Demo/DemoAssets/BallDemo/**

- HitAnimation.anim · Animation · 12 KB
- IdleAnimation.anim · Animation · 1 KB
- NVBallAnimatorController.controller · Animator · 4 KB

**Assets/_ThirdParty/Feel/NiceVibrations/Demo/DemoAssets/BallDemo/Physics2DMaterials/**

- NVBouncy.physicsMaterial2D · Other · 270 B

**Assets/_ThirdParty/Feel/NiceVibrations/Demo/DemoAssets/BallDemo/Sprites/**

- NVBall.png · Texture · 3 KB
- NVBallParticle.mat · Material · 2 KB
- NVBallParticle.png · Texture · 14 KB

**Assets/_ThirdParty/Feel/NiceVibrations/Demo/DemoAssets/CarDemo/Sounds/**

- NVCarEngine.wav · Audio · 689 KB

**Assets/_ThirdParty/Feel/NiceVibrations/Demo/DemoAssets/CarDemo/Sprites/**

- NVCar.png · Texture · 12 KB
- NVCarWheel.png · Texture · 1 KB

**Assets/_ThirdParty/Feel/NiceVibrations/Demo/DemoAssets/HapticClipsDemo/Animations/**

- AHAPPresetIconAnimator.controller · Animator · 7 KB
- IdleAnimation.anim · Animation · 1 KB
- ShakingAnimation.anim · Animation · 5 KB
- SwapAnimation.anim · Animation · 20 KB

**Assets/_ThirdParty/Feel/NiceVibrations/Demo/DemoAssets/HapticClipsDemo/Haptics/**

- NVCarillon.haptic · Other · 9 KB
- NVDice.haptic · Other · 3 KB
- NVDrumsLoop.haptic · Other · 3 KB
- NVGameOver.haptic · Other · 11 KB
- NVHeartbeats.haptic · Other · 6 KB
- NVLaser.haptic · Other · 9 KB
- NVPowerOff.haptic · Other · 5 KB
- NVReload.haptic · Other · 9 KB
- NVTeleport.haptic · Other · 3 KB

**Assets/_ThirdParty/Feel/NiceVibrations/Demo/DemoAssets/HapticClipsDemo/Sounds/**

- NVCarillon.wav · Audio · 440 KB
- NVDice.wav · Audio · 166 KB
- NVDrumsLoop.wav · Audio · 150 KB
- NVGameOver.wav · Audio · 357 KB
- NVHeartbeats.wav · Audio · 310 KB
- NVLaser.wav · Audio · 404 KB
- NVPowerOff.wav · Audio · 725 KB
- NVReload.wav · Audio · 513 KB
- NVTeleport.wav · Audio · 45 KB

**Assets/_ThirdParty/Feel/NiceVibrations/Demo/DemoAssets/HapticClipsDemo/Sprites/**

- NVPresetsIcons.png · Texture · 46 KB

**Assets/_ThirdParty/Feel/NiceVibrations/Demo/DemoAssets/RegularPresetsDemo/Sprites/**

- RegularPresetsIcons.png · Texture · 59 KB

**Assets/_ThirdParty/Feel/NiceVibrations/Demo/DemoAssets/WobbleDemo/Animations/**

- WobbleButtonAnimator.controller · Animator · 4 KB
- WobbleIdle.anim · Animation · 1 KB
- WobbleSpark.anim · Animation · 12 KB

**Assets/_ThirdParty/Feel/NiceVibrations/Demo/DemoAssets/WobbleDemo/Haptics/**

- NVWobbleSpring.haptic · Other · 12 KB

**Assets/_ThirdParty/Feel/NiceVibrations/Demo/DemoAssets/WobbleDemo/Prefabs/**

- WobbleButton.prefab · Prefab · 13 KB

**Assets/_ThirdParty/Feel/NiceVibrations/Demo/DemoAssets/WobbleDemo/Sounds/**

- NVWobbleSpring.wav · Audio · 296 KB

**Assets/_ThirdParty/Feel/NiceVibrations/HapticSamples/ApplicationUX/**

- Alert1.haptic · Other · 2 KB
- Alert1.wav · Audio · 346 KB
- Award1.haptic · Other · 6 KB
- Award1.wav · Audio · 81 KB
- Beep1.haptic · Other · 1 KB
- Beep1.wav · Audio · 31 KB
- Beep2.haptic · Other · 2 KB
- Beep2.wav · Audio · 16 KB
- Beep3.haptic · Other · 3 KB
- Beep3.wav · Audio · 16 KB
- Button1.haptic · Other · 1 KB
- Button1.wav · Audio · 8 KB
- Button2.haptic · Other · 931 B
- Button2.wav · Audio · 6 KB
- Button3.haptic · Other · 3 KB
- Button3.wav · Audio · 13 KB
- Button4.haptic · Other · 3 KB
- Button4.wav · Audio · 41 KB
- CameraShutter1.haptic · Other · 2 KB
- CameraShutter1.wav · Audio · 19 KB
- Hover1.haptic · Other · 1 KB
- Hover1.wav · Audio · 45 KB
- Hover2.haptic · Other · 2 KB
- Hover2.wav · Audio · 40 KB
- Pop1.haptic · Other · 823 B
- Pop1.wav · Audio · 13 KB

**Assets/_ThirdParty/Feel/NiceVibrations/HapticSamples/Footsteps/**

- FootstepGrass1.haptic · Other · 2 KB
- FootstepGrass1.wav · Audio · 86 KB
- FootstepGrass2.haptic · Other · 2 KB
- FootstepGrass2.wav · Audio · 86 KB
- FootstepGrass3.haptic · Other · 3 KB
- FootstepGrass3.wav · Audio · 62 KB
- FootstepGravel1.haptic · Other · 5 KB
- FootstepGravel1.wav · Audio · 32 KB
- FootstepGravel2.haptic · Other · 4 KB
- FootstepGravel2.wav · Audio · 26 KB
- FootstepGravel3.haptic · Other · 4 KB
- FootstepGravel3.wav · Audio · 22 KB
- FootstepSnow1.haptic · Other · 2 KB
- FootstepSnow1.wav · Audio · 37 KB

**Assets/_ThirdParty/Feel/NiceVibrations/HapticSamples/Impacts/**

- Body1.haptic · Other · 1 KB
- Body1.wav · Audio · 53 KB
- Body2.haptic · Other · 1 KB
- Body2.wav · Audio · 32 KB
- Body3.haptic · Other · 2 KB
- Body3.wav · Audio · 23 KB
- BulletCrack1.haptic · Other · 1 KB
- BulletCrack1.wav · Audio · 255 KB
- BulletCrack2.haptic · Other · 1 KB
- BulletCrack2.wav · Audio · 183 KB
- BulletCrack3.haptic · Other · 2 KB
- BulletCrack3.wav · Audio · 173 KB
- BulletMetal1.haptic · Other · 1 KB
- BulletMetal1.wav · Audio · 44 KB
- CarCrash1.haptic · Other · 2 KB
- CarCrash1.wav · Audio · 46 KB
- CombatPunch1.haptic · Other · 1009 B
- CombatPunch1.wav · Audio · 15 KB
- CombatPunch2.haptic · Other · 1 KB
- CombatPunch2.wav · Audio · 24 KB
- Concrete1.haptic · Other · 1 KB
- Concrete1.wav · Audio · 18 KB
- Concrete2.haptic · Other · 1 KB
- Concrete2.wav · Audio · 36 KB
- Concrete3.haptic · Other · 2 KB
- Concrete3.wav · Audio · 72 KB
- Glass1.haptic · Other · 4 KB
- Glass1.wav · Audio · 146 KB
- Glass2.haptic · Other · 3 KB
- Glass2.wav · Audio · 99 KB
- Glass3.haptic · Other · 9 KB
- Glass3.wav · Audio · 177 KB
- ImpactMetal1.haptic · Other · 4 KB
- ImpactMetal1.wav · Audio · 203 KB
- Punch1.haptic · Other · 1 KB
- Punch1.wav · Audio · 49 KB
- Wood1.haptic · Other · 1 KB
- Wood1.wav · Audio · 99 KB
- Wood2.haptic · Other · 1 KB
- Wood2.wav · Audio · 67 KB
- Wood3.haptic · Other · 1 KB
- Wood3.wav · Audio · 93 KB

**Assets/_ThirdParty/Feel/NiceVibrations/HapticSamples/Music/**

- Cymbal1.haptic · Other · 3 KB
- Cymbal1.wav · Audio · 14 KB
- Cymbal2.haptic · Other · 4 KB
- Cymbal2.wav · Audio · 115 KB
- Cymbal3.haptic · Other · 4 KB
- Cymbal3.wav · Audio · 108 KB
- Drums1.haptic · Other · 27 KB
- Drums1.wav · Audio · 646 KB
- Drums2.haptic · Other · 18 KB
- Drums2.wav · Audio · 844 KB
- Kick1.haptic · Other · 2 KB
- Kick1.wav · Audio · 35 KB
- Kick2.haptic · Other · 3 KB
- Kick2.wav · Audio · 51 KB
- Snare1.haptic · Other · 3 KB
- Snare1.wav · Audio · 24 KB
- Tom1.haptic · Other · 3 KB
- Tom1.wav · Audio · 69 KB
- Tom2.haptic · Other · 2 KB
- Tom2.wav · Audio · 71 KB

**Assets/_ThirdParty/Feel/NiceVibrations/HapticSamples/Nature/**

- CatPurr1.haptic · Other · 25 KB
- CatPurr1.wav · Audio · 1.3 MB
- CreatureRoar1.haptic · Other · 5 KB
- CreatureRoar1.wav · Audio · 117 KB
- Rain1.haptic · Other · 29 KB
- Rain1.wav · Audio · 1.2 MB
- RaininCar1.haptic · Other · 44 KB
- RaininCar1.wav · Audio · 1.3 MB
- RainonUmbrella1.haptic · Other · 20 KB
- RainonUmbrella1.wav · Audio · 425 KB
- Splash1.haptic · Other · 9 KB
- Splash1.wav · Audio · 297 KB
- Thunder1.haptic · Other · 19 KB
- Thunder1.wav · Audio · 696 KB
- Thunder2.haptic · Other · 46 KB
- Thunder2.wav · Audio · 991 KB

**Assets/_ThirdParty/Feel/NiceVibrations/HapticSamples/Objects/**

- Book1.haptic · Other · 1 KB
- Book1.wav · Audio · 87 KB
- Book2.haptic · Other · 1 KB
- Book2.wav · Audio · 87 KB
- Book3.haptic · Other · 4 KB
- Book3.wav · Audio · 149 KB
- Cash1.haptic · Other · 6 KB
- Cash2.haptic · Other · 4 KB
- Cash2.wav · Audio · 120 KB
- Cash5.wav · Audio · 78 KB
- Cash8.wav · Audio · 186 KB
- CashRegister1.haptic · Other · 7 KB
- Coin1.haptic · Other · 4 KB
- Coin1.wav · Audio · 52 KB
- Coin2.haptic · Other · 3 KB
- Coin3.haptic · Other · 2 KB
- Coin4.haptic · Other · 3 KB
- Coin5.haptic · Other · 2 KB
- Coin6.haptic · Other · 2 KB
- Coins2.wav · Audio · 27 KB
- Coins3.wav · Audio · 26 KB
- Coins4.wav · Audio · 37 KB
- Coins5.wav · Audio · 33 KB
- Coins7.wav · Audio · 26 KB
- DoorKnob1.haptic · Other · 3 KB
- DoorKnob1.wav · Audio · 30 KB
- Fan1.haptic · Other · 6 KB
- Fan1.wav · Audio · 259 KB
- Fan2.haptic · Other · 8 KB
- Fan2.wav · Audio · 259 KB
- Fireplace1.haptic · Other · 7 KB
- Fireplace1.wav · Audio · 172 KB
- Fireplace2.haptic · Other · 12 KB
- Fireplace2.wav · Audio · 172 KB
- Flashlight1.haptic · Other · 1 KB
- Flashlight1.wav · Audio · 86 KB
- Flashlight2.haptic · Other · 1 KB
- Flashlight2.wav · Audio · 86 KB
- Hammer1.haptic · Other · 2 KB
- Hammer1.wav · Audio · 86 KB
- Hammer2.haptic · Other · 1 KB
- Hammer2.wav · Audio · 86 KB
- Hammer3.haptic · Other · 1 KB
- Hammer3.wav · Audio · 86 KB
- Keyboard1.haptic · Other · 2 KB
- Keyboard1.wav · Audio · 43 KB
- Keyboard2.haptic · Other · 2 KB
- Keyboard2.wav · Audio · 43 KB
- Keyboard3.haptic · Other · 2 KB
- Keyboard3.wav · Audio · 43 KB
- Keychain1.haptic · Other · 1 KB
- Keys1.haptic · Other · 1 KB
- Keys1.wav · Audio · 86 KB
- Keys2.wav · Audio · 43 KB
- Pen1.haptic · Other · 2 KB
- Pen1.wav · Audio · 43 KB
- Switch6.wav · Audio · 22 KB
- SwitchHeavy1.haptic · Other · 1 KB
- SwitchHeavy1.wav · Audio · 22 KB
- SwitchHeavy2.haptic · Other · 2 KB
- SwitchHeavy2.wav · Audio · 22 KB
- SwitchLight1.haptic · Other · 1 KB
- SwitchLight1.wav · Audio · 22 KB
- SwitchLight2.haptic · Other · 1 KB
- SwitchLight2.wav · Audio · 22 KB
- SwitchLight3.haptic · Other · 1 KB
- SwitchLight3.wav · Audio · 22 KB
- SwitchMed1.haptic · Other · 1 KB
- SwitchMed1.wav · Audio · 22 KB
- SwitchMed2.haptic · Other · 1 KB
- SwitchMed2.wav · Audio · 22 KB
- Velcro1.haptic · Other · 4 KB
- Velcro1.wav · Audio · 86 KB
- Velcro2.haptic · Other · 2 KB
- Velcro2.wav · Audio · 86 KB
- Woodsaw1.haptic · Other · 2 KB
- Woodsaw1.wav · Audio · 86 KB
- Woodsaw2.haptic · Other · 2 KB
- Woodsaw2.wav · Audio · 43 KB
- Woodsaw3.haptic · Other · 2 KB
- Woodsaw3.wav · Audio · 43 KB

**Assets/_ThirdParty/Feel/NiceVibrations/HapticSamples/SoundFX/**

- Boost1.haptic · Other · 4 KB
- Boost1.wav · Audio · 123 KB
- CarEngine1.haptic · Other · 28 KB
- CarEngine1.wav · Audio · 491 KB
- CarEngine2.haptic · Other · 109 KB
- CarEngine2.wav · Audio · 1.1 MB
- CarScreech1.haptic · Other · 6 KB
- CarScreech1.wav · Audio · 129 KB
- CarScreech2.haptic · Other · 9 KB
- CarScreech2.wav · Audio · 249 KB
- Crumbling1.haptic · Other · 6 KB
- Crumbling1.wav · Audio · 155 KB
- IceCubes1.haptic · Other · 3 KB
- IceCubes1.wav · Audio · 270 KB
- MovieProjector1.haptic · Other · 24 KB
- MovieProjector1.wav · Audio · 345 KB

**Assets/_ThirdParty/Feel/NiceVibrations/HapticSamples/Weapons/**

- BowRelease1.haptic · Other · 1 KB
- BowRelease1.wav · Audio · 31 KB
- DistanceGunShot1.haptic · Other · 2 KB
- DistanceGunShot1.wav · Audio · 103 KB
- DistanceGunShot2.haptic · Other · 3 KB
- DistanceGunShot2.wav · Audio · 142 KB
- DistanceGunShot3.haptic · Other · 2 KB
- DistanceGunShot3.wav · Audio · 102 KB
- HeavyMachineGunFire1.haptic · Other · 2 KB
- HeavyMachineGunFire1.wav · Audio · 92 KB
- HeavyMachineGunFire2.haptic · Other · 7 KB
- HeavyMachineGunFire2.wav · Audio · 246 KB
- HeavyMachineGunLoad1.haptic · Other · 2 KB
- HeavyMachineGunLoad1.wav · Audio · 72 KB
- LightMachineGunFire1.haptic · Other · 12 KB
- LightMachineGunFire1.wav · Audio · 310 KB
- LightMachineGunLoad1.haptic · Other · 7 KB
- LightMachineGunLoad1.wav · Audio · 240 KB
- PistolFire1.haptic · Other · 1 KB
- PistolFire1.wav · Audio · 59 KB
- PistolFire2.haptic · Other · 2 KB
- PistolFire2.wav · Audio · 40 KB
- PistolFire3.haptic · Other · 1 KB
- PistolFire3.wav · Audio · 72 KB
- PistolLoad1.haptic · Other · 2 KB
- PistolLoad1.wav · Audio · 15 KB
- PistolLoad2.haptic · Other · 2 KB
- PistolLoad2.wav · Audio · 40 KB
- PistolLoad3.haptic · Other · 2 KB
- PistolLoad3.wav · Audio · 77 KB
- ScifiGunshot1.haptic · Other · 1 KB
- ScifiGunshot1.wav · Audio · 25 KB
- ShotgunFire1.haptic · Other · 2 KB
- ShotgunFire1.wav · Audio · 106 KB
- ShotgunFire2.haptic · Other · 3 KB
- ShotgunFire2.wav · Audio · 227 KB
- ShotgunFire3.haptic · Other · 3 KB
- ShotgunFire3.wav · Audio · 350 KB
- ShotgunLoad1.haptic · Other · 2 KB
- ShotgunLoad1.wav · Audio · 70 KB
- ShotgunLoad2.haptic · Other · 3 KB
- ShotgunLoad2.wav · Audio · 38 KB
- ShotgunLoad3.haptic · Other · 3 KB
- ShotgunLoad3.wav · Audio · 37 KB
- ShotgunShell1.haptic · Other · 2 KB
- ShotgunShell1.wav · Audio · 67 KB
- SniperFire1.haptic · Other · 2 KB
- SniperFire1.wav · Audio · 166 KB
