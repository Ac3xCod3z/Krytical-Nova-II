using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using RipVertices.QuickDestruction.Editor;

namespace RipVertices.QuickDestruction.Tests
{
    public sealed class StructuralRegressionTests
    {
        const BindingFlags PrivateStatic =
            BindingFlags.Static | BindingFlags.NonPublic;
        const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void RootHierarchyCollectsEveryGeneratedDestructibleInBatchContainer()
        {
            var container = new GameObject("Modules_Destructibles");
            GameObject first = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject second = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                first.name = "Wall_Destructible";
                second.name = "Floor_Destructible";
                first.transform.SetParent(container.transform, false);
                second.transform.SetParent(container.transform, false);
                first.AddComponent<DestructibleRoot>();
                second.AddComponent<DestructibleRoot>();

                List<GameObject> sources = QuickDestructionWindow.CollectSourceCandidates(
                    new[] { container },
                    QuickDestructionWindow.SourceSelectionMode.RootHierarchy);

                CollectionAssert.AreEquivalent(new[] { first, second }, sources,
                    "Selecting the generated Root + Nested container must preview every " +
                    "destructible child instead of falling back to the first baked object.");
            }
            finally
            {
                Object.DestroyImmediate(container);
            }
        }

        [Test]
        public void ProceduralMeshParticlesKeepSharpPieceTextureWithValidUvs()
        {
            var parent = new GameObject("Particle Test Parent");
            try
            {
                ParticleSystem system =
                    DestructionPresetEffectLibrary.CreateParticleSystem(parent.transform);
                Assert.That(system, Is.Not.Null);

                DestructionPresetEffectLibrary.ConfigureParticleSystem(system,
                    FracturePattern.UniformConcrete, 1f, 1f, Color.gray, true);

                ParticleSystemRenderer renderer =
                    system.GetComponent<ParticleSystemRenderer>();
                Assert.That(renderer, Is.Not.Null);
                Assert.That(renderer.renderMode, Is.EqualTo(ParticleSystemRenderMode.Mesh));
                Assert.That(renderer.mesh, Is.Not.Null);
                Assert.That(renderer.sharedMaterial, Is.Not.Null);
                Assert.That(DestructionPresetEffectLibrary.IsMaterialPipelineCompatible(
                    renderer.sharedMaterial), Is.True);

                Texture particleTexture = null;
                string[] textureProperties =
                {
                    "_UnlitColorMap", "_BaseColorMap", "_BaseMap", "_MainTex"
                };
                for (int i = 0; i < textureProperties.Length; i++)
                {
                    string property = textureProperties[i];
                    if (!renderer.sharedMaterial.HasProperty(property)) continue;
                    Texture assigned = renderer.sharedMaterial.GetTexture(property);
                    if (assigned != null)
                    {
                        particleTexture = assigned;
                        break;
                    }
                }
                Assert.That(particleTexture, Is.Not.Null,
                    "Procedural mesh particle lost its Sharp_Piece texture.");
                Assert.That(particleTexture.name, Does.Contain("Sharp_Piece"));

                Vector2[] uvs = renderer.mesh.uv;
                Assert.That(uvs, Has.Length.EqualTo(renderer.mesh.vertexCount));
                for (int i = 0; i < uvs.Length; i++)
                {
                    Assert.That(float.IsNaN(uvs[i].x) || float.IsNaN(uvs[i].y), Is.False);
                    Assert.That(uvs[i].x, Is.InRange(0.48f, 0.62f));
                    Assert.That(uvs[i].y, Is.InRange(0.62f, 0.78f));
                }
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void ParticleTintPreservesMaterialAndUsesOneVisibleColorMultiplier()
        {
            var parent = new GameObject("Particle Tint Test Parent");
            try
            {
                ParticleSystem system =
                    DestructionPresetEffectLibrary.CreateParticleSystem(parent.transform);
                Assert.That(system, Is.Not.Null);
                system.gameObject.SetActive(true);
                DestructionPresetEffectLibrary.ConfigureParticleSystem(system,
                    FracturePattern.UniformConcrete, 1f, 1f, Color.gray, true);

                ParticleSystemRenderer renderer =
                    system.GetComponent<ParticleSystemRenderer>();
                QuickDestructionParticleTint tint =
                    system.GetComponent<QuickDestructionParticleTint>();
                Assert.That(renderer, Is.Not.Null);
                Assert.That(tint, Is.Not.Null);

                Material authoredMaterial = renderer.sharedMaterial;
                bool usesMaterialTint = authoredMaterial != null &&
                    (authoredMaterial.HasProperty("_UnlitColor") ||
                     authoredMaterial.HasProperty("_BaseColor") ||
                     authoredMaterial.HasProperty("_Color") ||
                     authoredMaterial.HasProperty("_TintColor"));
                Color authoredBase = authoredMaterial.HasProperty("_BaseColor")
                    ? authoredMaterial.GetColor("_BaseColor") : Color.white;
                tint.CorrectLinearColorSpace = false;
                tint.BrightnessBoost = 1f;
                tint.MinimumLuminance = 0.3f;
                tint.SampledColorWeight = 1f;
                tint.ApplyImpactColor(new Color(0.1f, 0.08f, 0.06f, 1f), 1f);

                Assert.That(renderer.sharedMaterial, Is.SameAs(authoredMaterial),
                    "Tinting must not replace or instantiate the configured material.");
                if (authoredMaterial.HasProperty("_BaseColor"))
                    Assert.That(authoredMaterial.GetColor("_BaseColor"),
                        Is.EqualTo(authoredBase),
                        "Tinting must not rewrite the authored material color.");

                var main = system.main;
                Color vertexTint = main.startColor.color;
                if (usesMaterialTint)
                {
                    Assert.That(vertexTint.r, Is.EqualTo(1f).Within(0.001f));
                    Assert.That(vertexTint.g, Is.EqualTo(1f).Within(0.001f));
                    Assert.That(vertexTint.b, Is.EqualTo(1f).Within(0.001f));
                }
                var lifetime = system.colorOverLifetime;
                Color lifetimeStart = lifetime.color.gradient.Evaluate(0f);
                Assert.That(lifetimeStart.r, Is.EqualTo(1f).Within(0.001f));
                Assert.That(lifetimeStart.g, Is.EqualTo(1f).Within(0.001f));
                Assert.That(lifetimeStart.b, Is.EqualTo(1f).Within(0.001f));

                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                Color applied = usesMaterialTint
                    ? authoredMaterial.HasProperty("_BaseColor")
                        ? block.GetColor("_BaseColor")
                        : authoredMaterial.HasProperty("_UnlitColor")
                            ? block.GetColor("_UnlitColor")
                            : authoredMaterial.HasProperty("_Color")
                                ? block.GetColor("_Color")
                                : block.GetColor("_TintColor")
                    : vertexTint;
                float luminance = 0.2126f * applied.r +
                                  0.7152f * applied.g +
                                  0.0722f * applied.b;
                Assert.That(luminance, Is.GreaterThanOrEqualTo(0.299f),
                    "The configured minimum luminance must affect the visible tint.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void EffectPoolDefersNativeTintBlockUntilLifecycleInitialization()
        {
            var poolObject = new GameObject("FX Pool Initialization Test");
            try
            {
                System.Type poolType = typeof(DestructionBreakEffects).Assembly.GetType(
                    "RipVertices.QuickDestruction.DestructionEffectPool");
                Assert.That(poolType, Is.Not.Null);

                Component pool = null;
                Assert.DoesNotThrow(() => pool = poolObject.AddComponent(poolType),
                    "Adding the pool component must not construct native Unity objects " +
                    "from a MonoBehaviour field initializer.");
                Assert.That(pool, Is.Not.Null);

                FieldInfo tintBlockField = poolType.GetField(
                    "m_particleTintBlock", PrivateInstance);
                Assert.That(tintBlockField, Is.Not.Null);
                Assert.That(tintBlockField.GetValue(pool), Is.Null,
                    "Native Unity objects must not be created by the component constructor.");

                MethodInfo ensureTintBlock = poolType.GetMethod(
                    "EnsureParticleTintBlock", PrivateInstance);
                Assert.That(ensureTintBlock, Is.Not.Null);
                Assert.DoesNotThrow(() => ensureTintBlock.Invoke(pool, null));
                Assert.That(tintBlockField.GetValue(pool),
                    Is.TypeOf<MaterialPropertyBlock>(),
                    "Awake/lazy initialization must create the shared tint block safely.");

                PropertyInfo hideFlagsProperty = poolType.GetProperty(
                    "PooledObjectHideFlags", PrivateStatic);
                Assert.That(hideFlagsProperty, Is.Not.Null);
                var editorFlags = (HideFlags)hideFlagsProperty.GetValue(null, null);
                Assert.That(editorFlags & HideFlags.DontSaveInEditor,
                    Is.EqualTo(HideFlags.None),
                    "Editor pool objects must participate in normal Play Mode teardown.");
                Assert.That(editorFlags & HideFlags.HideInHierarchy,
                    Is.EqualTo(HideFlags.HideInHierarchy));
            }
            finally
            {
                Object.DestroyImmediate(poolObject);
            }
        }

        [Test]
        public void ProfilesUseFiniteUnsupportedSpanByDefaultAndAfterPreset()
        {
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            try
            {
                Assert.That(profile.MaxUnsupportedSpan, Is.EqualTo(3f));
                profile.MaxUnsupportedSpan = 0f;
                profile.ApplyPreset(FracturePattern.UniformConcrete);
                Assert.That(profile.MaxUnsupportedSpan, Is.EqualTo(3f));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void AttachedRendererStateUsesCommonProbeAndSeamlessShadowMode()
        {
            var primaryObject = new GameObject("Primary");
            var detailObject = new GameObject("Detail");
            var anchorObject = new GameObject("Anchor");
            try
            {
                detailObject.transform.SetParent(primaryObject.transform, false);
                var primary = primaryObject.AddComponent<MeshRenderer>();
                var detail = detailObject.AddComponent<MeshRenderer>();
                var fragment = new FragmentRecord
                {
                    Renderer = primary,
                    ProbeRenderers = new List<Renderer> { primary, detail },
                    DynamicShadowCastingModes = new List<ShadowCastingMode>
                    {
                        ShadowCastingMode.On,
                        ShadowCastingMode.TwoSided
                    }
                };

                MethodInfo setAnchor = typeof(DestructibleRoot).GetMethod(
                    "SetFragmentProbeAnchor", PrivateStatic);
                MethodInfo setShadow = typeof(DestructibleRoot).GetMethod(
                    "SetFragmentShadowMode", PrivateStatic);
                Assert.That(setAnchor, Is.Not.Null);
                Assert.That(setShadow, Is.Not.Null);

                setAnchor.Invoke(null, new object[] { fragment, anchorObject.transform });
                Assert.That(primary.probeAnchor, Is.SameAs(anchorObject.transform));
                Assert.That(detail.probeAnchor, Is.SameAs(anchorObject.transform));

                setShadow.Invoke(null, new object[] { fragment, false });
                Assert.That(primary.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
                Assert.That(detail.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));

                setAnchor.Invoke(null, new object[] { fragment, null });
                setShadow.Invoke(null, new object[] { fragment, true });
                Assert.That(primary.probeAnchor, Is.Null);
                Assert.That(detail.probeAnchor, Is.Null);
                Assert.That(primary.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On));
                Assert.That(detail.shadowCastingMode,
                    Is.EqualTo(ShadowCastingMode.TwoSided));
            }
            finally
            {
                Object.DestroyImmediate(anchorObject);
                Object.DestroyImmediate(primaryObject);
            }
        }

        [Test]
        public void RotatedWallBuildsCompleteProbeGridOnItsOrientedBase()
        {
            var rootObject = new GameObject("Rotated Wall Root");
            var colliderObject = new GameObject("Rotated Wall Collider");
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            try
            {
                colliderObject.transform.SetParent(rootObject.transform, false);
                colliderObject.transform.localPosition = new Vector3(0f, 10f, 0f);
                colliderObject.transform.localRotation = Quaternion.Euler(0f, 37f, 15f);
                var box = colliderObject.AddComponent<BoxCollider>();
                box.size = new Vector3(20f, 20f, 0.4f);

                profile.SupportDetection = SupportDetectionMode.AutomaticBelow;
                profile.SupportProbeGrid = 3;
                var root = rootObject.AddComponent<DestructibleRoot>();
                var node = rootObject.AddComponent<StructuralSupportNode>();
                root.Profile = profile;
                root.StructuralNode = node;
                node.Root = root;
                node.ProbeCollider = box;
                node.ProbeColliders = new List<Collider> { box };

                node.RebuildSupportBaseline();

                FieldInfo originsField = typeof(StructuralSupportNode).GetField(
                    "m_probeOriginsLocal", PrivateInstance);
                Assert.That(originsField, Is.Not.Null);
                var origins = (IList)originsField.GetValue(node);
                Assert.That(origins.Count, Is.EqualTo(25));

                for (int i = 0; i < origins.Count; i++)
                {
                    Vector3 rootLocal = (Vector3)origins[i];
                    Vector3 world = rootObject.transform.TransformPoint(rootLocal);
                    Vector3 boxLocal = colliderObject.transform.InverseTransformPoint(world);
                    Assert.That(boxLocal.y, Is.EqualTo(-9.96f).Within(0.08f));
                    Assert.That(Mathf.Abs(boxLocal.x), Is.LessThanOrEqualTo(10.01f));
                    Assert.That(Mathf.Abs(boxLocal.z), Is.LessThanOrEqualTo(0.21f));
                }
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void SharedFoundationThresholdUsesAllWorldFootingsAtDifferentHeights()
        {
            MethodInfo belowThreshold = typeof(StructuralSupportNode).GetMethod(
                "FoundationSupportIsBelowThreshold", PrivateStatic);
            Assert.That(belowThreshold, Is.Not.Null);
            Assert.That((bool)belowThreshold.Invoke(null,
                new object[] { 18, 9, 0.5f }), Is.False,
                "Exactly the configured share of the combined foundation must still hold.");
            Assert.That((bool)belowThreshold.Invoke(null,
                new object[] { 18, 8, 0.5f }), Is.True,
                "Losing the shared foundation threshold must be able to collapse the root.");
        }

        [Test]
        public void CarriedFloorProbesStayOutOfSharedFoundationGroup()
        {
            var rootObject = new GameObject("Shared Foundation Root");
            var lowerGroundObject = new GameObject("Lower Ground");
            var upperGroundObject = new GameObject("Upper Ground");
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            try
            {
                BoxCollider AddBox(GameObject owner, string name, Vector3 center,
                    Vector3 size)
                {
                    var child = new GameObject(name);
                    child.transform.SetParent(owner.transform, false);
                    child.transform.position = center;
                    var collider = child.AddComponent<BoxCollider>();
                    collider.size = size;
                    return collider;
                }

                lowerGroundObject.transform.position = new Vector3(-2f, -0.5f, 0f);
                var lowerGround = lowerGroundObject.AddComponent<BoxCollider>();
                lowerGround.size = new Vector3(1.5f, 1f, 1.5f);
                upperGroundObject.transform.position = new Vector3(2f, 0.5f, 0f);
                var upperGround = upperGroundObject.AddComponent<BoxCollider>();
                upperGround.size = new Vector3(1.5f, 1f, 1.5f);

                BoxCollider lowerWall = AddBox(rootObject, "Lower Wall",
                    new Vector3(-2f, 0.5f, 0f), Vector3.one);
                BoxCollider upperWall = AddBox(rootObject, "Upper Wall",
                    new Vector3(2f, 1.5f, 0f), Vector3.one);
                BoxCollider carriedFloor = AddBox(rootObject, "Carried Floor",
                    new Vector3(-2f, 1.1f, 0f), new Vector3(1f, 0.2f, 1f));

                profile.IntactBody = IntactBodyMode.ReleaseWhenUnsupported;
                profile.SupportDetection = SupportDetectionMode.AutomaticBelow;
                profile.SupportProbeDistance = 0.35f;
                profile.SupportProbeGrid = 2;
                profile.RequiredSupportRatio = 0.5f;
                profile.SupportGraceTime = 0f;
                profile.MaxUnsupportedSpan = 0f;

                var root = rootObject.AddComponent<DestructibleRoot>();
                var node = rootObject.AddComponent<StructuralSupportNode>();
                var intactBody = rootObject.AddComponent<Rigidbody>();
                intactBody.isKinematic = true;
                root.Profile = profile;
                root.StructuralNode = node;
                root.IntactBody = intactBody;
                node.Root = root;
                node.IntactBody = intactBody;
                node.ProbeCollider = lowerWall;
                node.ProbeColliders = new List<Collider>
                {
                    lowerWall, upperWall, carriedFloor
                };

                Physics.SyncTransforms();
                node.RebuildSupportBaseline();

                FieldInfo groundedField = typeof(StructuralSupportNode).GetField(
                    "m_groupGrounded", PrivateInstance);
                FieldInfo groupInitialField = typeof(StructuralSupportNode).GetField(
                    "m_groupInitialUnits", PrivateInstance);
                Assert.That(groundedField, Is.Not.Null);
                Assert.That(groupInitialField, Is.Not.Null);
                var grounded = (List<bool>)groundedField.GetValue(node);
                var groupInitial = (List<int>)groupInitialField.GetValue(node);

                Assert.That(grounded, Is.EqualTo(new[] { true, true, false }),
                    "Ground contacts at different Y levels join the foundation; the floor does not.");
                Assert.That(groupInitial[2], Is.GreaterThan(0),
                    "The carried floor must still retain its own local support baseline.");
                Assert.That(node.InitialSupportUnits, Is.EqualTo(18),
                    "The public footing total must include two 3x3 wall grids, not the carried floor.");

                MethodInfo updateFoundation = typeof(StructuralSupportNode).GetMethod(
                    "UpdateFoundationGroup", PrivateInstance);
                Assert.That(updateFoundation, Is.Not.Null);

                lowerGround.enabled = false;
                Physics.SyncTransforms();
                bool releasedAtHalf = (bool)updateFoundation.Invoke(node,
                    new object[] { profile });

                Assert.That(releasedAtHalf, Is.False);
                Assert.That(node.CurrentSupportUnits, Is.EqualTo(9));
                Assert.That(intactBody.isKinematic, Is.True,
                    "Exactly 50% of the shared foundation must keep the body attached.");

                upperGround.enabled = false;
                Physics.SyncTransforms();
                bool releasedBelowThreshold = (bool)updateFoundation.Invoke(node,
                    new object[] { profile });

                Assert.That(releasedBelowThreshold, Is.True);
                Assert.That(intactBody.isKinematic, Is.False,
                    "Crossing the combined foundation threshold must release the whole body.");
                Assert.That(node.Released, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
                Object.DestroyImmediate(lowerGroundObject);
                Object.DestroyImmediate(upperGroundObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void DirectHitContextsCarryExactStructuralGroup()
        {
            var damage = new DamageContext(10f, Vector3.one, Vector3.up,
                0.75f, 0, 7, 2f, Vector3.forward, -1f, 3);
            var directBreak = new BreakContext(Vector3.one, Vector3.up,
                0.75f, 2f, 7, Vector3.forward, -1f, 3);
            var radialBreak = new BreakContext(Vector3.zero, Vector3.up,
                3f, 20f);

            Assert.That(damage.TargetStructuralGroup, Is.EqualTo(3));
            Assert.That(directBreak.TargetStructuralGroup, Is.EqualTo(3));
            Assert.That(radialBreak.TargetStructuralGroup, Is.EqualTo(-1));
        }

        [Test]
        public void DynamicPropKeepsGroupedUntouchedRemainderOnPhysicalCarrier()
        {
            var rootObject = new GameObject("Dynamic Barrel Root");
            var intactObject = new GameObject("Intact");
            var fracturedObject = new GameObject("Fractured");
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            try
            {
                intactObject.transform.SetParent(rootObject.transform, false);
                fracturedObject.transform.SetParent(rootObject.transform, false);
                var intactBody = intactObject.AddComponent<Rigidbody>();
                intactBody.useGravity = true;
                intactBody.isKinematic = false;

                profile.IntactBody = IntactBodyMode.Dynamic;
                profile.ActivateAllOnFirstBreak = false;
                profile.EnforceActiveBodyBudget = false;
                profile.FragmentPushMultiplier = 0f;

                var root = rootObject.AddComponent<DestructibleRoot>();
                root.Profile = profile;
                root.IntactRoot = intactObject;
                root.FracturedRoot = fracturedObject;
                root.IntactBody = intactBody;
                root.Fragments = new List<FragmentRecord>();

                for (int i = 0; i < 3; i++)
                {
                    GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    piece.name = "Barrel Ring " + i;
                    piece.transform.SetParent(fracturedObject.transform, false);
                    piece.transform.localPosition = Vector3.up * i;
                    var collider = piece.GetComponent<BoxCollider>();
                    var body = piece.AddComponent<Rigidbody>();
                    body.useGravity = true;
                    body.isKinematic = true;
                    root.Fragments.Add(new FragmentRecord
                    {
                        Transform = piece.transform,
                        Body = body,
                        Collider = collider,
                        Renderer = piece.GetComponent<MeshRenderer>(),
                        InitialLocalPosition = piece.transform.localPosition,
                        InitialLocalRotation = Quaternion.identity,
                        InitialLocalScale = Vector3.one,
                        Volume = 1f,
                        Mass = 1f,
                        StructuralGroup = 0,
                        SupportNeighbours = new List<int>()
                    });
                }

                root.Fragments[0].SupportNeighbours.Add(1);
                root.Fragments[1].SupportNeighbours.AddRange(new[] { 0, 2 });
                root.Fragments[2].SupportNeighbours.Add(1);
                Physics.SyncTransforms();

                root.BreakNow(new BreakContext(Vector3.down * 0.5f, Vector3.down,
                    0.1f, 10f, 7, Vector3.down, 0f, 0));

                Assert.That(root.Fragments[0].Body.isKinematic, Is.False,
                    "The directly struck bottom fragment must become loose debris.");
                Assert.That(root.Fragments[1].Activated, Is.True,
                    "A grouped direct hit must not leave the untouched Dynamic remainder as a frozen shell.");
                Assert.That(root.Fragments[2].Activated, Is.True);

                MethodInfo isClusterMember = typeof(DestructibleRoot).GetMethod(
                    "IsDynamicClusterMember", PrivateInstance);
                Assert.That(isClusterMember, Is.Not.Null);
                Assert.That((bool)isClusterMember.Invoke(root, new object[] { 1 }), Is.True);
                Assert.That((bool)isClusterMember.Invoke(root, new object[] { 2 }), Is.True);
                Assert.That(root.Fragments[1].Body.isKinematic &&
                            root.Fragments[2].Body.isKinematic, Is.False,
                    "The connected upper remainder needs one live Rigidbody carrier so gravity can move it.");
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void IntactColliderMapResolvesOneStructuralModuleOnly()
        {
            var rootObject = new GameObject("Mapped Root");
            var firstObject = new GameObject("First Wall");
            var secondObject = new GameObject("Second Wall");
            var fragmentObject = new GameObject("Fractured Wall");
            try
            {
                firstObject.transform.SetParent(rootObject.transform, false);
                secondObject.transform.SetParent(rootObject.transform, false);
                fragmentObject.transform.SetParent(rootObject.transform, false);
                firstObject.transform.localPosition = Vector3.left * 5f;
                secondObject.transform.localPosition = Vector3.right * 5f;
                var first = firstObject.AddComponent<BoxCollider>();
                var second = secondObject.AddComponent<BoxCollider>();
                var fragmentCollider = fragmentObject.AddComponent<BoxCollider>();
                var root = rootObject.AddComponent<DestructibleRoot>();
                root.IntactColliders = new List<Collider> { first, second };
                root.IntactColliderStructuralGroups = new List<int> { 4, 9 };
                root.Fragments = new List<FragmentRecord>
                {
                    new FragmentRecord
                    {
                        Transform = fragmentObject.transform,
                        Collider = fragmentCollider,
                        StructuralGroup = 12
                    }
                };
                var runtime = fragmentObject.AddComponent<FragmentRuntime>();
                runtime.Root = root;
                runtime.FragmentIndex = 0;

                Assert.That(root.ResolveStructuralGroup(first), Is.EqualTo(4));
                Assert.That(root.ResolveStructuralGroup(second), Is.EqualTo(9));
                Assert.That(root.ResolveStructuralGroup(fragmentCollider),
                    Is.EqualTo(12));
                Assert.That(root.ResolveStructuralGroup(Vector3.left * 5f),
                    Is.EqualTo(4));
                Assert.That(root.ResolveStructuralGroup(Vector3.right * 5f),
                    Is.EqualTo(9));
                Assert.That(root.ResolveStructuralGroup(null), Is.EqualTo(-1));
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        public void PrimaryRuntimeIndexIsRepairedOnlyByExactTransformIdentity()
        {
            var rootObject = new GameObject("Identity Root");
            var firstObject = new GameObject("Fragment 0");
            var secondObject = new GameObject("Fragment 1");
            var foreignObject = new GameObject("Foreign Fragment");
            try
            {
                firstObject.transform.SetParent(rootObject.transform, false);
                secondObject.transform.SetParent(rootObject.transform, false);
                foreignObject.transform.SetParent(rootObject.transform, false);
                var root = rootObject.AddComponent<DestructibleRoot>();
                root.Fragments = new List<FragmentRecord>
                {
                    new FragmentRecord { Transform = firstObject.transform },
                    new FragmentRecord { Transform = secondObject.transform }
                };
                var stale = firstObject.AddComponent<FragmentRuntime>();
                stale.Root = root;
                stale.FragmentIndex = 1;
                var foreign = foreignObject.AddComponent<FragmentRuntime>();
                foreign.Root = root;
                foreign.FragmentIndex = 0;

                MethodInfo resolve = typeof(DestructibleRoot).GetMethod(
                    "ResolvePrimaryFragmentIndex", PrivateInstance);
                Assert.That(resolve, Is.Not.Null);
                Assert.That(resolve.Invoke(root, new object[] { stale }),
                    Is.EqualTo(0));
                Assert.That(resolve.Invoke(root, new object[] { foreign }),
                    Is.EqualTo(-1));
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        public void DeferredCompoundCollisionRoutesOnlyExactDisabledFragment()
        {
            var rootObject = new GameObject("Deferred Collision Root");
            var firstObject = new GameObject("Fragment 0");
            var secondObject = new GameObject("Fragment 1");
            var foreignObject = new GameObject("Foreign Fragment");
            try
            {
                firstObject.transform.SetParent(rootObject.transform, false);
                secondObject.transform.SetParent(rootObject.transform, false);
                foreignObject.transform.SetParent(rootObject.transform, false);
                var firstCollider = firstObject.AddComponent<BoxCollider>();
                var secondCollider = secondObject.AddComponent<BoxCollider>();
                var foreignCollider = foreignObject.AddComponent<BoxCollider>();
                var root = rootObject.AddComponent<DestructibleRoot>();
                root.DeferInactiveFragmentPhysics = true;
                root.Fragments = new List<FragmentRecord>
                {
                    new FragmentRecord
                    {
                        Transform = firstObject.transform,
                        Collider = firstCollider
                    },
                    new FragmentRecord
                    {
                        Transform = secondObject.transform,
                        Collider = secondCollider
                    }
                };

                var firstRuntime = firstObject.AddComponent<FragmentRuntime>();
                firstRuntime.Root = root;
                firstRuntime.FragmentIndex = 0;
                firstRuntime.enabled = false;
                var secondRuntime = secondObject.AddComponent<FragmentRuntime>();
                secondRuntime.Root = root;
                secondRuntime.FragmentIndex = 1;
                secondRuntime.enabled = true;
                var foreignRuntime = foreignObject.AddComponent<FragmentRuntime>();
                foreignRuntime.Root = root;
                foreignRuntime.FragmentIndex = 0;
                foreignRuntime.enabled = false;

                FieldInfo state = typeof(DestructibleRoot).GetField(
                    "m_state", PrivateInstance);
                MethodInfo resolve = typeof(DestructibleRoot).GetMethod(
                    "ResolveDeferredFragmentCollisionIndex", PrivateInstance);
                Assert.That(state, Is.Not.Null);
                Assert.That(resolve, Is.Not.Null);

                state.SetValue(root, DestructibleState.Fractured);
                Assert.That(resolve.Invoke(root, new object[] { firstCollider }),
                    Is.EqualTo(0),
                    "The disabled deferred fragment under the compound body must receive the hit.");
                Assert.That(resolve.Invoke(root, new object[] { secondCollider }),
                    Is.EqualTo(-1),
                    "An enabled runtime receives OnCollisionEnter itself and must not be hit twice.");
                Assert.That(resolve.Invoke(root, new object[] { foreignCollider }),
                    Is.EqualTo(-1),
                    "A stale or foreign index must never activate another fragment or wall.");

                state.SetValue(root, DestructibleState.Intact);
                Assert.That(resolve.Invoke(root, new object[] { firstCollider }),
                    Is.EqualTo(-1),
                    "Intact collisions must retain the normal root damage path.");
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        public void LocalBreakImmediatelyReleasesDisconnectedSameGroupIsland()
        {
            var rootObject = new GameObject("Local Island Root");
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            try
            {
                profile.IntactBody = IntactBodyMode.ReleaseWhenUnsupported;
                profile.ActivateAllOnFirstBreak = false;
                profile.EnforceActiveBodyBudget = false;
                profile.RequiredSupportRatio = 0f;
                profile.MaxUnsupportedSpan = 0f;

                var root = rootObject.AddComponent<DestructibleRoot>();
                root.Profile = profile;
                root.Fragments = new List<FragmentRecord>();

                FragmentRecord AddPiece(string name, float x, int group,
                    bool groundAnchor, params int[] neighbours)
                {
                    var piece = new GameObject(name);
                    piece.transform.SetParent(rootObject.transform, false);
                    piece.transform.position = new Vector3(x, 2f, 0f);
                    var collider = piece.AddComponent<BoxCollider>();
                    collider.size = Vector3.one * 0.8f;
                    var body = piece.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    var record = new FragmentRecord
                    {
                        Transform = piece.transform,
                        Collider = collider,
                        Body = body,
                        Volume = 0.512f,
                        Mass = 1f,
                        StructuralGroup = group,
                        GroundAnchor = groundAnchor,
                        SupportNeighbours = new List<int>(neighbours)
                    };
                    root.Fragments.Add(record);
                    return record;
                }

                FragmentRecord anchor = AddPiece("Anchor", 0f, 0, true, 1);
                FragmentRecord bridge = AddPiece("Destroyed Bridge", 1f, 0,
                    false, 0, 2);
                FragmentRecord islandA = AddPiece("Floating Island A", 2f, 0,
                    false, 1, 3);
                FragmentRecord islandB = AddPiece("Floating Island B", 3f, 0,
                    false, 2);
                FragmentRecord otherWall = AddPiece("Other Wall", 100f, 1,
                    true);

                FieldInfo state = typeof(DestructibleRoot).GetField(
                    "m_state", PrivateInstance);
                Assert.That(state, Is.Not.Null);
                state.SetValue(root, DestructibleState.Fractured);
                Physics.SyncTransforms();

                root.BreakNow(new BreakContext(bridge.Collider.bounds.center,
                    Vector3.forward, 0.05f, 0f, 17, Vector3.forward, 0f, 0));

                Assert.That(bridge.Activated, Is.True,
                    "The exact struck bridge must be removed first.");
                Assert.That(anchor.Activated, Is.False,
                    "The surviving footing stays attached.");
                Assert.That(islandA.Activated, Is.True,
                    "A same-group shard with no path to an anchor must fall immediately.");
                Assert.That(islandB.Activated, Is.True,
                    "The complete disconnected island must fall in the same event.");
                Assert.That(otherWall.Activated, Is.False,
                    "An unrelated structural group must never inherit this local break.");
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
                Object.DestroyImmediate(profile);
            }
        }

        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(0, false)]
        [TestCase(1, false)]
        public void LocalColumnHitReleasesOnlyArchIslandsThatLostTheirCarrier(
            int hitSide, bool cutArchFirst)
        {
            var rootObject = new GameObject("Arch Support Regression");
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            try
            {
                profile.IntactBody = IntactBodyMode.ReleaseWhenUnsupported;
                profile.ActivateAllOnFirstBreak = false;
                profile.EnforceActiveBodyBudget = false;
                profile.RequiredSupportRatio = 0f;
                profile.MaxUnsupportedSpan = 0f;
                var root = rootObject.AddComponent<DestructibleRoot>();
                root.Profile = profile;
                root.Fragments = new List<FragmentRecord>();

                FragmentRecord AddBox(Vector3 position, Vector3 size,
                    int group, bool anchor, params int[] neighbours)
                {
                    var piece = new GameObject("Piece " + root.Fragments.Count);
                    piece.transform.SetParent(rootObject.transform, false);
                    piece.transform.position = position;
                    var collider = piece.AddComponent<BoxCollider>();
                    collider.size = size;
                    var body = piece.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    var record = new FragmentRecord
                    {
                        Transform = piece.transform, Collider = collider, Body = body,
                        Volume = size.x * size.y * size.z, Mass = 1f,
                        StructuralGroup = group, GroundAnchor = anchor,
                        SupportNeighbours = new List<int>(neighbours)
                    };
                    root.Fragments.Add(record);
                    return record;
                }

                for (int side = 0; side < 2; side++)
                {
                    float x = side == 0 ? -2f : 2f;
                    int first = side * 3;
                    AddBox(new Vector3(x, 0.5f, 0f), Vector3.one,
                        side, true, first + 1);
                    AddBox(new Vector3(x, 1.5f, 0f), Vector3.one,
                        side, false, first, first + 2);
                    AddBox(new Vector3(x, 2.5f, 0f), Vector3.one,
                        side, false, first + 1);
                }
                FragmentRecord leftArch = AddBox(new Vector3(-1.5f, 3.5f, 0f),
                    new Vector3(2f, 1f, 1f), 2, false, 7);
                FragmentRecord archCentre = AddBox(new Vector3(0f, 3.5f, 0f),
                    Vector3.one, 2, false, 6, 8);
                FragmentRecord rightArch = AddBox(new Vector3(1.5f, 3.5f, 0f),
                    new Vector3(2f, 1f, 1f), 2, false, 7);
                typeof(DestructibleRoot).GetField("m_state", PrivateInstance)
                    .SetValue(root, DestructibleState.Fractured);
                Physics.SyncTransforms();

                void Hit(FragmentRecord fragment)
                {
                    root.BreakNow(new BreakContext(fragment.Collider.bounds.center,
                        Vector3.forward, 0.05f, 0f, 17, Vector3.forward, 0f,
                        fragment.StructuralGroup));
                }

                if (cutArchFirst)
                {
                    Hit(archCentre);
                    Assert.That(archCentre.Activated, Is.True);
                    Assert.That(leftArch.Activated || rightArch.Activated, Is.False,
                        "Both arch halves still have their own column after the centre hit.");
                }

                Hit(root.Fragments[hitSide * 3 + 1]);
                FragmentRecord unsupportedArch = hitSide == 0 ? leftArch : rightArch;
                FragmentRecord supportedArch = hitSide == 0 ? rightArch : leftArch;
                Assert.That(root.Fragments[hitSide * 3].Activated, Is.False);
                Assert.That(root.Fragments[hitSide * 3 + 2].Activated, Is.True,
                    "The severed column top must fall during the local hit.");
                Assert.That(unsupportedArch.Activated, Is.EqualTo(cutArchFirst),
                    "An isolated arch half must fall with its column; a continuous span still has a carrier.");
                Assert.That(supportedArch.Activated, Is.False,
                    "The other arch half and its intact column remain supported.");
                Assert.That(root.Fragments[(1 - hitSide) * 3 + 2].Activated, Is.False);

                Hit(root.Fragments[(1 - hitSide) * 3 + 1]);
                Assert.That(leftArch.Activated && rightArch.Activated, Is.True,
                    "After the final carrier is severed, both arch halves must fall.");
                Assert.That(archCentre.Activated, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void SectionWeightsCoverEveryHeightOccupiedByATallFragment()
        {
            var fragmentObject = new GameObject("Tall Structural Fragment");
            try
            {
                fragmentObject.transform.position = new Vector3(0f, 5f, 0f);
                var collider = fragmentObject.AddComponent<BoxCollider>();
                collider.size = new Vector3(1f, 10f, 1f);
                var fragment = new FragmentRecord
                {
                    Transform = fragmentObject.transform,
                    Collider = collider,
                    Volume = 10f
                };
                FieldInfo bandsField = typeof(DestructibleRoot).GetField(
                    "SectionBands", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo accumulate = typeof(DestructibleRoot).GetMethod(
                    "AccumulateSectionWeights", PrivateStatic);
                Assert.That(bandsField, Is.Not.Null);
                Assert.That(accumulate, Is.Not.Null);
                int bandCount = (int)bandsField.GetRawConstantValue();
                var bands = new float[bandCount];

                accumulate.Invoke(null, new object[] { bands, fragment, 0f, 10f });

                Assert.That(bands, Has.All.GreaterThan(0f));
                float sum = 0f;
                for (int i = 0; i < bands.Length; i++) sum += bands[i];
                Assert.That(sum, Is.EqualTo(fragment.Volume).Within(0.0001f));

                // The same bounds-based calculation is also used by the one-shot 4x
                // refinement and must conserve volume at that resolution as well.
                var refinedBands = new float[bandCount * 4];
                accumulate.Invoke(null,
                    new object[] { refinedBands, fragment, 0f, 10f });
                Assert.That(refinedBands, Has.All.GreaterThan(0f));
                float refinedSum = 0f;
                for (int i = 0; i < refinedBands.Length; i++)
                    refinedSum += refinedBands[i];
                Assert.That(refinedSum,
                    Is.EqualTo(fragment.Volume).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(fragmentObject);
            }
        }

        [Test]
        public void RefinedSectionDetectsNarrowCutWithoutWakingAnotherGroup()
        {
            var rootObject = new GameObject("Adaptive Section Root");
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            try
            {
                var root = rootObject.AddComponent<DestructibleRoot>();
                root.Profile = profile;
                root.Fragments = new List<FragmentRecord>();

                FragmentRecord AddBox(string name, float centerY, float height,
                    float volume, int group)
                {
                    var piece = new GameObject(name);
                    piece.transform.SetParent(rootObject.transform, false);
                    piece.transform.position = new Vector3(group * 3f,
                        centerY, 0f);
                    var collider = piece.AddComponent<BoxCollider>();
                    collider.size = new Vector3(1f, height, 1f);
                    var body = piece.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    var record = new FragmentRecord
                    {
                        Transform = piece.transform,
                        Collider = collider,
                        Body = body,
                        Volume = volume,
                        Mass = 1f,
                        StructuralGroup = group
                    };
                    root.Fragments.Add(record);
                    return record;
                }

                // Group zero spans exactly 16 metres. Its coarse bottom band is one metre.
                // Removing only the first 25 cm leaves 75% in that coarse band, but leaves
                // the first 64-band refinement slice completely empty below a real top load.
                FragmentRecord narrowBase = AddBox("Narrow removable footing",
                    0.125f, 0.25f, 0.25f, 0);
                FragmentRecord remainingBase = AddBox("Remaining coarse-band footing",
                    0.625f, 0.75f, 0.75f, 0);
                FragmentRecord topLoad = AddBox("Top load", 15.5f, 1f, 1f, 0);
                FragmentRecord otherGroup = AddBox("Independent wall", 8f,
                    16f, 16f, 1);

                FieldInfo state = typeof(DestructibleRoot).GetField(
                    "m_state", PrivateInstance);
                MethodInfo weakest = typeof(DestructibleRoot).GetMethod(
                    "GetGroupWeakestSectionRatio", PrivateInstance, null,
                    new[] { typeof(int), typeof(int).MakeByRefType() }, null);
                MethodInfo release = typeof(DestructibleRoot).GetMethod(
                    "ReleaseGroupAboveSection", PrivateInstance);
                Assert.That(state, Is.Not.Null);
                Assert.That(weakest, Is.Not.Null);
                Assert.That(release, Is.Not.Null);
                state.SetValue(root, DestructibleState.Fractured);

                object[] intactArgs = { 0, -1 };
                Assert.That((float)weakest.Invoke(root, intactArgs), Is.EqualTo(1f));

                narrowBase.Activated = true;
                object[] cutArgs = { 0, -1 };
                float ratio = (float)weakest.Invoke(root, cutArgs);
                int failedBand = (int)cutArgs[1];

                Assert.That(ratio, Is.EqualTo(0f).Within(0.0001f),
                    "The empty 25 cm slice must override the still-healthy 75% coarse band.");
                Assert.That(ratio, Is.LessThan(profile.RequiredSupportRatio));
                Assert.That(failedBand, Is.GreaterThanOrEqualTo(0));

                int released = (int)release.Invoke(root,
                    new object[] { 0, failedBand, Vector3.zero });

                Assert.That(released, Is.GreaterThanOrEqualTo(2));
                Assert.That(remainingBase.Activated, Is.True,
                    "Material above the exact refined cut must be released.");
                Assert.That(topLoad.Activated, Is.True);
                Assert.That(otherGroup.Activated, Is.False,
                    "A refined result for group zero must never wake group one.");
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ProbeThresholdIsAuthoritativeWhenSpanLimitIsDisabled()
        {
            var rootObject = new GameObject("Authoritative Probe Root");
            var groundObject = new GameObject("Temporary Test Ground");
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            try
            {
                var ground = groundObject.AddComponent<BoxCollider>();
                ground.size = new Vector3(10f, 1f, 10f);

                var probeObject = new GameObject("Broad Floor Probe");
                probeObject.transform.SetParent(rootObject.transform, false);
                probeObject.transform.position = Vector3.up;
                var probe = probeObject.AddComponent<BoxCollider>();
                probe.size = new Vector3(4f, 0.4f, 4f);

                profile.IntactBody = IntactBodyMode.ReleaseWhenUnsupported;
                profile.SupportDetection = SupportDetectionMode.AutomaticBelow;
                profile.SupportProbeDistance = 0.35f;
                profile.SupportProbeGrid = 2;
                profile.RequiredSupportRatio = 0.5f;
                profile.SupportGraceTime = 0f;
                profile.MaxUnsupportedSpan = 0f;

                var root = rootObject.AddComponent<DestructibleRoot>();
                var node = rootObject.AddComponent<StructuralSupportNode>();
                root.Profile = profile;
                root.StructuralNode = node;
                root.Fragments = new List<FragmentRecord>();
                node.Root = root;
                node.ProbeCollider = probe;
                node.ProbeColliders = new List<Collider> { probe };

                for (int side = 0; side < 2; side++)
                {
                    var piece = new GameObject($"Floor Half {side}");
                    piece.transform.SetParent(rootObject.transform, false);
                    piece.transform.position = new Vector3(
                        side == 0 ? -1f : 1f, 1f, 0f);
                    var collider = piece.AddComponent<BoxCollider>();
                    collider.size = new Vector3(2f, 0.4f, 4f);
                    var body = piece.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    root.Fragments.Add(new FragmentRecord
                    {
                        Transform = piece.transform,
                        Collider = collider,
                        Body = body,
                        Volume = 3.2f,
                        Mass = 1f,
                        StructuralGroup = 0,
                        GroundAnchor = true,
                        SupportNeighbours = new List<int> { 1 - side }
                    });
                }

                Physics.SyncTransforms();
                node.RebuildSupportBaseline();
                Assert.That(node.InitialSupportUnits, Is.GreaterThan(0));

                FieldInfo state = typeof(DestructibleRoot).GetField(
                    "m_state", PrivateInstance);
                MethodInfo capture = typeof(StructuralSupportNode).GetMethod(
                    "CaptureFracturedBaselineBeforeActivation", PrivateInstance);
                MethodInfo update = typeof(StructuralSupportNode).GetMethod(
                    "UpdateStructuralGroups", PrivateInstance);
                Assert.That(state, Is.Not.Null);
                Assert.That(capture, Is.Not.Null);
                Assert.That(update, Is.Not.Null);
                state.SetValue(root, DestructibleState.Fractured);
                capture.Invoke(node, null);

                // Remove every measured footing. The baked neighbour graph still has a
                // path to its GroundAnchor records, and MaxUnsupportedSpan=0 disables the
                // optional reach rule. RequiredSupportRatio must nevertheless be final.
                ground.enabled = false;
                Physics.SyncTransforms();
                update.Invoke(node, new object[] { profile });

                Assert.That(root.Fragments[0].Activated, Is.True);
                Assert.That(root.Fragments[1].Activated, Is.True,
                    "A surviving graph path must not override a failed probe threshold.");
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
                Object.DestroyImmediate(groundObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ImmediateBreakAfterResetKeepsNonZeroProbeBaseline()
        {
            var rootObject = new GameObject("Immediate Break Root");
            var groundObject = new GameObject("Immediate Break Ground");
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            try
            {
                var ground = groundObject.AddComponent<BoxCollider>();
                groundObject.transform.position = new Vector3(0f, -0.5f, 0f);
                ground.size = new Vector3(10f, 1f, 10f);

                var intactRoot = new GameObject("Intact");
                intactRoot.transform.SetParent(rootObject.transform, false);
                var probe = intactRoot.AddComponent<BoxCollider>();
                intactRoot.transform.position = new Vector3(0f, 0.5f, 0f);
                probe.size = Vector3.one;

                var fracturedRoot = new GameObject("Fractured");
                fracturedRoot.transform.SetParent(rootObject.transform, false);
                var fragmentObject = new GameObject("Fragment");
                fragmentObject.transform.SetParent(fracturedRoot.transform, false);
                fragmentObject.transform.position = new Vector3(0f, 0.5f, 0f);
                var fragmentCollider = fragmentObject.AddComponent<BoxCollider>();
                fragmentCollider.size = Vector3.one;
                var fragmentBody = fragmentObject.AddComponent<Rigidbody>();
                fragmentBody.isKinematic = true;
                fracturedRoot.SetActive(false);

                profile.IntactBody = IntactBodyMode.ReleaseWhenUnsupported;
                profile.SupportDetection = SupportDetectionMode.AutomaticBelow;
                profile.SupportProbeDistance = 0.35f;
                profile.SupportProbeGrid = 2;
                profile.RequiredSupportRatio = 0.5f;

                var root = rootObject.AddComponent<DestructibleRoot>();
                var node = rootObject.AddComponent<StructuralSupportNode>();
                root.Profile = profile;
                root.IntactRoot = intactRoot;
                root.FracturedRoot = fracturedRoot;
                root.StructuralNode = node;
                root.Fragments = new List<FragmentRecord>
                {
                    new FragmentRecord
                    {
                        Transform = fragmentObject.transform,
                        Collider = fragmentCollider,
                        Body = fragmentBody,
                        Volume = 1f,
                        Mass = 1f,
                        StructuralGroup = 0,
                        GroundAnchor = true
                    }
                };
                node.Root = root;
                node.ProbeCollider = probe;
                node.ProbeColliders = new List<Collider> { probe };

                Physics.SyncTransforms();
                // No RebuildSupportBaseline call: this is the real timing window after
                // Reset and before StructuralSupportNode receives its next FixedUpdate.
                root.BreakNow(new BreakContext(fragmentCollider.bounds.center,
                    Vector3.forward, 0.1f, 0f, 0, Vector3.forward, 0f, 0));

                FieldInfo initialField = typeof(StructuralSupportNode).GetField(
                    "m_groupInitialUnits", PrivateInstance);
                FieldInfo requiredField = typeof(StructuralSupportNode).GetField(
                    "m_groupRequiredUnits", PrivateInstance);
                Assert.That(initialField, Is.Not.Null);
                Assert.That(requiredField, Is.Not.Null);
                var initial = (List<int>)initialField.GetValue(node);
                var required = (List<int>)requiredField.GetValue(node);

                Assert.That(initial, Has.Count.EqualTo(1));
                Assert.That(initial[0], Is.GreaterThan(0),
                    "Hiding IntactRoot must not turn a fast first hit into a 0/0 probe baseline.");
                Assert.That(node.InitialSupportUnits, Is.GreaterThan(0),
                    "The shared foundation baseline must survive the same immediate first hit.");
                Assert.That(required[0], Is.EqualTo(
                    Mathf.CeilToInt(initial[0] * profile.RequiredSupportRatio)));
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
                Object.DestroyImmediate(groundObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ProbeCoverageCannotComeFromAnotherStructuralGroup()
        {
            var rootObject = new GameObject("Group Scoped Probe Root");
            var groundObject = new GameObject("Group Scoped Probe Ground");
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            try
            {
                var ground = groundObject.AddComponent<BoxCollider>();
                groundObject.transform.position = new Vector3(0f, -0.5f, 0f);
                ground.size = new Vector3(10f, 1f, 10f);

                var probeObject = new GameObject("Group Zero Probe");
                probeObject.transform.SetParent(rootObject.transform, false);
                probeObject.transform.position = new Vector3(0f, 0.5f, 0f);
                var probe = probeObject.AddComponent<BoxCollider>();
                probe.size = Vector3.one;

                profile.IntactBody = IntactBodyMode.ReleaseWhenUnsupported;
                profile.SupportDetection = SupportDetectionMode.AutomaticBelow;
                profile.SupportProbeDistance = 0.35f;
                profile.SupportProbeGrid = 2;
                profile.RequiredSupportRatio = 0.5f;

                var root = rootObject.AddComponent<DestructibleRoot>();
                var node = rootObject.AddComponent<StructuralSupportNode>();
                root.Profile = profile;
                root.StructuralNode = node;
                root.Fragments = new List<FragmentRecord>();
                node.Root = root;
                node.ProbeCollider = probe;
                node.ProbeColliders = new List<Collider> { probe };

                FragmentRecord ownFragment = null;
                for (int group = 0; group < 2; group++)
                {
                    var piece = new GameObject($"Overlapping Group {group}");
                    piece.transform.SetParent(rootObject.transform, false);
                    piece.transform.position = new Vector3(0f, 0.5f, 0f);
                    var collider = piece.AddComponent<BoxCollider>();
                    collider.size = Vector3.one;
                    var body = piece.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    var record = new FragmentRecord
                    {
                        Transform = piece.transform,
                        Collider = collider,
                        Body = body,
                        Volume = 1f,
                        Mass = 1f,
                        StructuralGroup = group,
                        GroundAnchor = group == 0
                    };
                    root.Fragments.Add(record);
                    if (group == 0) ownFragment = record;
                }

                Physics.SyncTransforms();
                node.RebuildSupportBaseline();
                FieldInfo state = typeof(DestructibleRoot).GetField(
                    "m_state", PrivateInstance);
                MethodInfo capture = typeof(StructuralSupportNode).GetMethod(
                    "CaptureFracturedBaselineBeforeActivation", PrivateInstance);
                MethodInfo count = typeof(StructuralSupportNode).GetMethod(
                    "CountGroupSupportUnits", PrivateInstance);
                Assert.That(state, Is.Not.Null);
                Assert.That(capture, Is.Not.Null);
                Assert.That(count, Is.Not.Null);
                state.SetValue(root, DestructibleState.Fractured);
                capture.Invoke(node, null);

                ownFragment.Activated = true;
                int current = (int)count.Invoke(node, new object[] { profile, 0 });

                Assert.That(current, Is.Zero,
                    "An overlapping fragment from group one must not count as group zero's material coverage.");
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
                Object.DestroyImmediate(groundObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void FailedMidHeightSectionReleasesOnlyItsOwnGroupAboveTheCut()
        {
            var rootObject = new GameObject("Section Threshold Root");
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            try
            {
                var root = rootObject.AddComponent<DestructibleRoot>();
                root.Profile = profile;
                root.Fragments = new List<FragmentRecord>();

                // Three courses in group zero. Two thirds of the middle course are
                // destroyed, leaving less than the authored 50% section threshold.
                for (int course = 0; course < 3; course++)
                {
                    for (int column = 0; column < 3; column++)
                    {
                        var piece = new GameObject($"G0 C{course} P{column}");
                        piece.transform.SetParent(rootObject.transform, false);
                        piece.transform.position = new Vector3(column * 1.1f,
                            1f + course * 4f, 0f);
                        var collider = piece.AddComponent<BoxCollider>();
                        var body = piece.AddComponent<Rigidbody>();
                        body.isKinematic = true;
                        root.Fragments.Add(new FragmentRecord
                        {
                            Transform = piece.transform,
                            Collider = collider,
                            Body = body,
                            Volume = 1f,
                            Mass = 1f,
                            StructuralGroup = 0
                        });
                    }
                }

                // A different wall overlaps the same height and must never be released by
                // group zero's section index.
                var other = new GameObject("Overlapping Other Group");
                other.transform.SetParent(rootObject.transform, false);
                other.transform.position = new Vector3(0f, 9f, 0.2f);
                var otherCollider = other.AddComponent<BoxCollider>();
                var otherBody = other.AddComponent<Rigidbody>();
                otherBody.isKinematic = true;
                var otherRecord = new FragmentRecord
                {
                    Transform = other.transform,
                    Collider = otherCollider,
                    Body = otherBody,
                    Volume = 1f,
                    Mass = 1f,
                    StructuralGroup = 1
                };
                root.Fragments.Add(otherRecord);

                FieldInfo state = typeof(DestructibleRoot).GetField(
                    "m_state", PrivateInstance);
                Assert.That(state, Is.Not.Null);
                state.SetValue(root, DestructibleState.Fractured);

                MethodInfo weakest = typeof(DestructibleRoot).GetMethod(
                    "GetGroupWeakestSectionRatio", PrivateInstance, null,
                    new[] { typeof(int), typeof(int).MakeByRefType() }, null);
                MethodInfo release = typeof(DestructibleRoot).GetMethod(
                    "ReleaseGroupAboveSection", PrivateInstance);
                Assert.That(weakest, Is.Not.Null);
                Assert.That(release, Is.Not.Null);

                // Capture the immutable baseline before removing the two middle pieces.
                object[] baselineArgs = { 0, -1 };
                Assert.That((float)weakest.Invoke(root, baselineArgs), Is.EqualTo(1f));
                root.Fragments[3].Activated = true;
                root.Fragments[4].Activated = true;
                object[] failedArgs = { 0, -1 };
                float ratio = (float)weakest.Invoke(root, failedArgs);
                int failedBand = (int)failedArgs[1];
                Assert.That(ratio, Is.LessThan(profile.RequiredSupportRatio));
                Assert.That(failedBand, Is.GreaterThanOrEqualTo(0));

                int released = (int)release.Invoke(root,
                    new object[] { 0, failedBand, Vector3.zero });

                Assert.That(released, Is.GreaterThan(0));
                Assert.That(root.Fragments[0].Activated, Is.False,
                    "The supported base below the failed section must stay attached.");
                Assert.That(root.Fragments[6].Activated, Is.True,
                    "Material above the failed section must be released.");
                Assert.That(otherRecord.Activated, Is.False,
                    "A coincident fragment in another structural group must stay attached.");
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void FailedSectionDropsDependentFloorOnlyAfterItsLastCarrierIsLost()
        {
            var rootObject = new GameObject("Section Dependency Root");
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            try
            {
                profile.IntactBody = IntactBodyMode.ReleaseWhenUnsupported;
                profile.SupportProbeDistance = 0.35f;
                profile.MaxUnsupportedSpan = 0f;

                var root = rootObject.AddComponent<DestructibleRoot>();
                root.Profile = profile;
                root.Fragments = new List<FragmentRecord>();

                FragmentRecord AddBox(string name, Vector3 position, Vector3 size,
                    int group, bool groundAnchor, params int[] neighbours)
                {
                    var piece = new GameObject(name);
                    piece.transform.SetParent(rootObject.transform, false);
                    piece.transform.position = position;
                    var collider = piece.AddComponent<BoxCollider>();
                    collider.size = size;
                    var body = piece.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    var record = new FragmentRecord
                    {
                        Transform = piece.transform,
                        Collider = collider,
                        Body = body,
                        Volume = size.x * size.y * size.z,
                        Mass = 1f,
                        StructuralGroup = group,
                        GroundAnchor = groundAnchor,
                        SupportNeighbours = new List<int>(neighbours)
                    };
                    root.Fragments.Add(record);
                    return record;
                }

                FragmentRecord lowerLeft = AddBox("Lower Left Wall",
                    new Vector3(-2f, 1f, 0f), new Vector3(1f, 2f, 1f),
                    0, true, 1);
                FragmentRecord upperLeft = AddBox("Upper Left Wall",
                    new Vector3(-2f, 3f, 0f), new Vector3(1f, 2f, 1f),
                    0, false, 0);
                FragmentRecord rightWall = AddBox("Independent Right Wall",
                    new Vector3(2f, 2f, 0f), new Vector3(1f, 4f, 1f),
                    1, true);
                FragmentRecord floor = AddBox("Carried Floor",
                    new Vector3(0f, 4.1f, 0f), new Vector3(5f, 0.2f, 1f),
                    2, false);

                FieldInfo state = typeof(DestructibleRoot).GetField(
                    "m_state", PrivateInstance);
                Assert.That(state, Is.Not.Null);
                state.SetValue(root, DestructibleState.Fractured);

                MethodInfo weakest = typeof(DestructibleRoot).GetMethod(
                    "GetGroupWeakestSectionRatio", PrivateInstance, null,
                    new[] { typeof(int) }, null);
                MethodInfo releaseAbove = typeof(DestructibleRoot).GetMethod(
                    "ReleaseGroupAboveSection", PrivateInstance);
                MethodInfo releaseUnsupported = typeof(DestructibleRoot).GetMethod(
                    "ReleaseUnsupportedIslandsNow", PrivateInstance);
                MethodInfo releaseGroup = typeof(DestructibleRoot).GetMethod(
                    "ReleaseStructuralGroup", PrivateInstance);
                Assert.That(weakest, Is.Not.Null);
                Assert.That(releaseAbove, Is.Not.Null);
                Assert.That(releaseUnsupported, Is.Not.Null);
                Assert.That(releaseGroup, Is.Not.Null);

                Assert.That((float)weakest.Invoke(root, new object[] { 0 }),
                    Is.EqualTo(1f));
                int releasedAboveCut = (int)releaseAbove.Invoke(root,
                    new object[] { 0, 8, new Vector3(-2f, 2f, 0f) });

                Assert.That(releasedAboveCut, Is.GreaterThan(0));
                Assert.That(lowerLeft.Activated, Is.False,
                    "The footing below the failed section must stay attached.");
                Assert.That(upperLeft.Activated, Is.True,
                    "Every load-bearing fragment crossing or above the failed section must fall.");

                releaseUnsupported.Invoke(root, new object[] { -1 });
                Assert.That(rightWall.Activated, Is.False);
                Assert.That(floor.Activated, Is.False,
                    "A floor with another real carrier must not fall merely because one wall was cut.");

                Assert.That((bool)releaseGroup.Invoke(root,
                    new object[] { 1, rightWall.Collider.bounds.center }), Is.True);
                Assert.That(rightWall.Activated, Is.True);
                Assert.That(floor.Activated, Is.True,
                    "The carried floor must fall after its last route to the foundation is lost.");
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void LocalCutInALongWallReleasesOnlyItsOwnSectionLane()
        {
            var rootObject = new GameObject("Long Wall Root");
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            try
            {
                var root = rootObject.AddComponent<DestructibleRoot>();
                root.Profile = profile;
                root.Fragments = new List<FragmentRecord>();

                // Twenty metres of wall, four courses of one-metre blocks. A hole punched
                // near one end is a local cut, never a horizontal section failure running
                // the entire length, and must not drop the far end with it.
                var blocks = new FragmentRecord[4, 20];
                for (int course = 0; course < 4; course++)
                {
                    for (int column = 0; column < 20; column++)
                    {
                        var piece = new GameObject("Course " + course +
                            " Column " + column);
                        piece.transform.SetParent(rootObject.transform, false);
                        piece.transform.position = new Vector3(column + 0.5f,
                            course + 0.5f, 0f);
                        var collider = piece.AddComponent<BoxCollider>();
                        var body = piece.AddComponent<Rigidbody>();
                        body.isKinematic = true;
                        var record = new FragmentRecord
                        {
                            Transform = piece.transform,
                            Collider = collider,
                            Body = body,
                            Volume = 1f,
                            Mass = 1f,
                            StructuralGroup = 0
                        };
                        root.Fragments.Add(record);
                        blocks[course, column] = record;
                    }
                }

                // EditMode does not guarantee that newly moved collider transforms are
                // visible to Collider.bounds until the physics scene is synchronized.
                Physics.SyncTransforms();

                FieldInfo state = typeof(DestructibleRoot).GetField(
                    "m_state", PrivateInstance);
                MethodInfo weakest = typeof(DestructibleRoot).GetMethod(
                    "GetGroupWeakestSectionRatio", PrivateInstance, null,
                    new[] { typeof(int), typeof(int).MakeByRefType() }, null);
                MethodInfo release = typeof(DestructibleRoot).GetMethod(
                    "ReleaseGroupAboveSection", PrivateInstance);
                Assert.That(state, Is.Not.Null);
                Assert.That(weakest, Is.Not.Null);
                Assert.That(release, Is.Not.Null);
                state.SetValue(root, DestructibleState.Fractured);

                object[] baselineArgs = { 0, -1 };
                Assert.That((float)weakest.Invoke(root, baselineArgs), Is.EqualTo(1f));

                // A hole three columns wide at the far end. A lane of a twenty-metre wall
                // holds barely more than one fragment, so judging that lane on its own
                // reported a severed section after a single shot and dropped the column
                // above it. The slice as a whole is still 85% intact and must not fail.
                for (int column = 17; column < 20; column++)
                    blocks[1, column].Activated = true;

                // The ratio is the gate: StructuralSupportNode only releases once it drops
                // below the authored threshold, so keeping it above is what stops the
                // chain reaction. The reported band is merely the weakest one, failed or not.
                object[] localArgs = { 0, -1 };
                float localRatio = (float)weakest.Invoke(root, localArgs);
                Assert.That(localRatio,
                    Is.GreaterThanOrEqualTo(profile.RequiredSupportRatio),
                    "A local hole must not register as a severed section.");

                // Now cut the same course across most of its length. The slice genuinely
                // fails, and only then may material come down — scoped to the lane that
                // actually lost the material, never the whole twenty metres.
                for (int column = 0; column < 14; column++)
                    blocks[1, column].Activated = true;

                object[] failedArgs = { 0, -1 };
                float ratio = (float)weakest.Invoke(root, failedArgs);
                int failedBand = (int)failedArgs[1];
                Assert.That(ratio, Is.LessThan(profile.RequiredSupportRatio),
                    "A slice that lost most of its material must fail.");
                Assert.That(failedBand, Is.GreaterThanOrEqualTo(0));

                int released = (int)release.Invoke(root,
                    new object[] { 0, failedBand, new Vector3(1f, 1.5f, 0f) });

                Assert.That(released, Is.GreaterThan(0));
                Assert.That(blocks[3, 0].Activated, Is.True,
                    "Material standing over the emptied lane must fall.");
                Assert.That(blocks[3, 19].Activated, Is.False,
                    "The far end never lost its own load path and must stay attached.");
                Assert.That(blocks[0, 0].Activated, Is.False,
                    "The footing below the cut is still standing on the ground.");
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void TouchingMaterialTheBakeNeverLinkedStaysSupported()
        {
            var rootObject = new GameObject("Split Graph Root");
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            try
            {
                profile.IntactBody = IntactBodyMode.ReleaseWhenUnsupported;
                profile.MaxUnsupportedSpan = 0f;

                var root = rootObject.AddComponent<DestructibleRoot>();
                root.Profile = profile;
                root.Fragments = new List<FragmentRecord>();

                FragmentRecord AddBox(string name, Vector3 position, Vector3 size,
                    int group, bool groundAnchor, params int[] neighbours)
                {
                    var piece = new GameObject(name);
                    piece.transform.SetParent(rootObject.transform, false);
                    piece.transform.position = position;
                    var collider = piece.AddComponent<BoxCollider>();
                    collider.size = size;
                    var body = piece.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    var record = new FragmentRecord
                    {
                        Transform = piece.transform,
                        Collider = collider,
                        Body = body,
                        Volume = size.x * size.y * size.z,
                        Mass = 1f,
                        StructuralGroup = group,
                        GroundAnchor = groundAnchor,
                        SupportNeighbours = new List<int>(neighbours)
                    };
                    root.Fragments.Add(record);
                    return record;
                }

                // A wall the bake did link, carrying a slab whose second half received no
                // baked link at all. Welded-vertex adjacency can miss such a face, and the
                // island was then unreachable for the support search from the very first
                // sweep — an untouched slab quietly shed a piece.
                AddBox("Wall Base", new Vector3(0f, 0.5f, 0f),
                    new Vector3(1f, 1f, 1f), 0, true, 1);
                AddBox("Wall Top", new Vector3(0f, 1.5f, 0f),
                    new Vector3(1f, 1f, 1f), 0, false, 0);
                FragmentRecord slabOnWall = AddBox("Slab Over Wall",
                    new Vector3(1f, 2.5f, 0f), new Vector3(3f, 1f, 1f), 1, false);
                FragmentRecord orphanSlab = AddBox("Slab Island Without A Baked Link",
                    new Vector3(4f, 2.5f, 0f), new Vector3(3f, 1f, 1f), 1, false);

                FieldInfo state = typeof(DestructibleRoot).GetField(
                    "m_state", PrivateInstance);
                Assert.That(state, Is.Not.Null);
                state.SetValue(root, DestructibleState.Fractured);

                MethodInfo releaseUnsupported = typeof(DestructibleRoot).GetMethod(
                    "ReleaseUnsupportedIslandsNow", PrivateInstance);
                Assert.That(releaseUnsupported, Is.Not.Null);

                releaseUnsupported.Invoke(root, new object[] { -1 });

                Assert.That(slabOnWall.Activated, Is.False,
                    "The slab resting on the wall keeps its route to the ground.");
                Assert.That(orphanSlab.Activated, Is.False,
                    "Material the bake never linked, but that abuts its own element, " +
                    "must not be dropped out of an otherwise untouched object.");
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
                Object.DestroyImmediate(profile);
            }
        }

        /// <summary>
        /// Builds a bare destructible whose only job is to stand under something else.
        /// </summary>
        static DestructibleRoot CreateCarrierPanel(string name, Vector3 center,
            Vector3 size, DestructionProfile profile, out BoxCollider collider)
        {
            var carrierObject = new GameObject(name);
            carrierObject.transform.position = center;
            collider = carrierObject.AddComponent<BoxCollider>();
            collider.size = size;
            var body = carrierObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            var carrier = carrierObject.AddComponent<DestructibleRoot>();
            carrier.Profile = profile;
            carrier.Fragments = new List<FragmentRecord>();
            return carrier;
        }

        static void MarkStructurallyReleased(DestructibleRoot root)
        {
            FieldInfo released = typeof(DestructibleRoot).GetField(
                "m_bodyReleased", PrivateInstance);
            Assert.That(released, Is.Not.Null);
            released.SetValue(root, true);
        }

        [Test]
        public void OverlappingNestedNeighbourCarriesProbesNoDownwardRayCanFind()
        {
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            DestructibleRoot carrier = null;
            var upperObject = new GameObject("Upper Panel");
            try
            {
                profile.IntactBody = IntactBodyMode.ReleaseWhenUnsupported;
                profile.SupportDetection = SupportDetectionMode.AutomaticBelow;
                profile.SupportProbeDistance = 0.35f;
                profile.SupportProbeGrid = 2;
                profile.RequiredSupportRatio = 0.5f;
                profile.SupportGraceTime = 0f;

                carrier = CreateCarrierPanel("Lower Panel", new Vector3(0f, 2f, 0f),
                    new Vector3(4f, 4f, 0.4f), profile, out BoxCollider _);

                // The upper panel sinks 10 cm into the lower one, which is how modular
                // walls are normally placed. Every probe origin then starts INSIDE the
                // panel below it, where a downward ray can never register a hit.
                var probeObject = new GameObject("Upper Panel Collider");
                probeObject.transform.SetParent(upperObject.transform, false);
                probeObject.transform.position = new Vector3(0f, 5.9f, 0f);
                var probe = probeObject.AddComponent<BoxCollider>();
                probe.size = new Vector3(4f, 4f, 0.4f);
                var upperBody = upperObject.AddComponent<Rigidbody>();
                upperBody.isKinematic = true;
                var upper = upperObject.AddComponent<DestructibleRoot>();
                var node = upperObject.AddComponent<StructuralSupportNode>();
                upper.Profile = profile;
                upper.StructuralNode = node;
                upper.IntactBody = upperBody;
                upper.Fragments = new List<FragmentRecord>();
                node.Root = upper;
                node.IntactBody = upperBody;
                node.ProbeCollider = probe;
                node.ProbeColliders = new List<Collider> { probe };

                Physics.SyncTransforms();
                node.RebuildSupportBaseline();

                FieldInfo groupInitialField = typeof(StructuralSupportNode).GetField(
                    "m_groupInitialUnits", PrivateInstance);
                FieldInfo dependsField = typeof(StructuralSupportNode).GetField(
                    "m_dependsOnDestructibleSupport", PrivateInstance);
                MethodInfo countGroup = typeof(StructuralSupportNode).GetMethod(
                    "CountGroupSupportUnits", PrivateInstance);
                Assert.That(groupInitialField, Is.Not.Null);
                Assert.That(dependsField, Is.Not.Null);
                Assert.That(countGroup, Is.Not.Null);

                var groupInitial = (List<int>)groupInitialField.GetValue(node);
                Assert.That(groupInitial[0], Is.GreaterThan(0),
                    "A panel carried by a neighbouring destructible must receive a real " +
                    "baseline. A zero baseline exempts it from support monitoring, which " +
                    "is exactly how it ends up hanging in mid-air.");
                Assert.That((bool)dependsField.GetValue(node), Is.True,
                    "The footing under this panel is itself destructible.");

                MarkStructurallyReleased(carrier);
                Physics.SyncTransforms();

                int remaining = (int)countGroup.Invoke(node,
                    new object[] { profile, 0 });
                Assert.That(remaining, Is.Zero,
                    "Once the neighbour that carried it is gone the panel must read as " +
                    "unsupported instead of keeping its baseline forever.");
            }
            finally
            {
                Object.DestroyImmediate(upperObject);
                if (carrier != null) Object.DestroyImmediate(carrier.gameObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ZeroThicknessNestedFloorUsesItsEdgeToFindWallSupport()
        {
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            var groupObject = new GameObject("Root Nested Output");
            try
            {
                profile.IntactBody = IntactBodyMode.ReleaseWhenUnsupported;
                profile.SupportDetection = SupportDetectionMode.AutomaticBelow;
                profile.SupportProbeDistance = 0.35f;
                profile.SupportProbeGrid = 3;
                profile.SupportProbeInset = 0.08f;
                profile.RequiredSupportRatio = 0.5f;
                profile.SupportGraceTime = 0f;

                // Root + Nested Meshes bakes an authored Unity Plane to a BoxCollider
                // whose thin axis is effectively zero. The floor meets this vertical wall
                // at x = -5 and has no collider underneath its interior.
                DestructibleRoot wall = CreateCarrierPanel("Thin Wall",
                    new Vector3(-5f, 3f, 0f), new Vector3(0f, 10f, 10f),
                    profile, out BoxCollider wallCollider);
                wall.transform.SetParent(groupObject.transform, true);

                var floorObject = new GameObject("Thin Floor");
                floorObject.transform.SetParent(groupObject.transform, true);
                var probeObject = new GameObject("Thin Floor Collider");
                probeObject.transform.SetParent(floorObject.transform, false);
                probeObject.transform.position = new Vector3(0f, 6f, 0f);
                var probe = probeObject.AddComponent<BoxCollider>();
                probe.size = new Vector3(10f, 0f, 10f);
                var floorBody = floorObject.AddComponent<Rigidbody>();
                floorBody.isKinematic = true;
                var floor = floorObject.AddComponent<DestructibleRoot>();
                var node = floorObject.AddComponent<StructuralSupportNode>();
                floor.Profile = profile;
                floor.StructuralNode = node;
                floor.IntactBody = floorBody;
                floor.Fragments = new List<FragmentRecord>();
                node.Root = floor;
                node.IntactBody = floorBody;
                node.ProbeCollider = probe;
                node.ProbeColliders = new List<Collider> { probe };

                Physics.SyncTransforms();
                node.RebuildSupportBaseline();

                FieldInfo groupInitialField = typeof(StructuralSupportNode).GetField(
                    "m_groupInitialUnits", PrivateInstance);
                MethodInfo countGroup = typeof(StructuralSupportNode).GetMethod(
                    "CountGroupSupportUnits", PrivateInstance);
                Assert.That(groupInitialField, Is.Not.Null);
                Assert.That(countGroup, Is.Not.Null);
                var groupInitial = (List<int>)groupInitialField.GetValue(node);
                Assert.That(groupInitial[0], Is.GreaterThan(0),
                    "A zero-thickness floor must sample its perimeter. Insetting those " +
                    "probes leaves a zero baseline, so the floor can never collapse.");

                wallCollider.enabled = false;
                MarkStructurallyReleased(wall);
                Physics.SyncTransforms();
                int remaining = (int)countGroup.Invoke(node,
                    new object[] { profile, 0 });
                Assert.That(remaining, Is.Zero,
                    "Once its nested wall is gone the floating floor has no support.");
            }
            finally
            {
                Object.DestroyImmediate(groupObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void LostNestedCarrierCollapsesTheIntactPanelIntoPieces()
        {
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            DestructibleRoot carrier = null;
            var upperObject = new GameObject("Stacked Panel");
            try
            {
                profile.IntactBody = IntactBodyMode.ReleaseWhenUnsupported;
                profile.SupportDetection = SupportDetectionMode.AutomaticBelow;
                profile.SupportProbeDistance = 0.35f;
                profile.SupportProbeGrid = 2;
                profile.RequiredSupportRatio = 0.5f;
                profile.SupportGraceTime = 0f;
                profile.EnforceActiveBodyBudget = false;

                carrier = CreateCarrierPanel("Carrier Panel", new Vector3(0f, 2f, 0f),
                    new Vector3(4f, 4f, 0.4f), profile, out BoxCollider carrierCollider);

                var probeObject = new GameObject("Stacked Panel Collider");
                probeObject.transform.SetParent(upperObject.transform, false);
                probeObject.transform.position = new Vector3(0f, 6f, 0f);
                var probe = probeObject.AddComponent<BoxCollider>();
                probe.size = new Vector3(4f, 4f, 0.4f);
                var upperBody = upperObject.AddComponent<Rigidbody>();
                upperBody.isKinematic = true;
                var upper = upperObject.AddComponent<DestructibleRoot>();
                var node = upperObject.AddComponent<StructuralSupportNode>();
                upper.Profile = profile;
                upper.StructuralNode = node;
                upper.IntactBody = upperBody;
                upper.Fragments = new List<FragmentRecord>();
                node.Root = upper;
                node.IntactBody = upperBody;
                node.ProbeCollider = probe;
                node.ProbeColliders = new List<Collider> { probe };

                for (int half = 0; half < 2; half++)
                {
                    var piece = new GameObject($"Panel Half {half}");
                    piece.transform.SetParent(upperObject.transform, false);
                    piece.transform.position = new Vector3(
                        half == 0 ? -1f : 1f, 6f, 0f);
                    var pieceCollider = piece.AddComponent<BoxCollider>();
                    pieceCollider.size = new Vector3(2f, 4f, 0.4f);
                    var pieceBody = piece.AddComponent<Rigidbody>();
                    pieceBody.isKinematic = true;
                    upper.Fragments.Add(new FragmentRecord
                    {
                        Transform = piece.transform,
                        Collider = pieceCollider,
                        Body = pieceBody,
                        Volume = 3.2f,
                        Mass = 1f,
                        StructuralGroup = 0,
                        GroundAnchor = half == 0,
                        SupportNeighbours = new List<int> { 1 - half }
                    });
                }

                Physics.SyncTransforms();
                node.RebuildSupportBaseline();
                Assert.That(node.InitialSupportUnits, Is.GreaterThan(0),
                    "The panel starts out standing on the carrier below it.");

                MethodInfo updateFoundation = typeof(StructuralSupportNode).GetMethod(
                    "UpdateFoundationGroup", PrivateInstance);
                Assert.That(updateFoundation, Is.Not.Null);

                carrierCollider.enabled = false;
                MarkStructurallyReleased(carrier);
                Physics.SyncTransforms();

                bool handled = (bool)updateFoundation.Invoke(node,
                    new object[] { profile });

                Assert.That(handled, Is.True);
                Assert.That(upper.State, Is.EqualTo(DestructibleState.Fractured),
                    "An element whose destructible footing was destroyed collapses. " +
                    "Sailing down as one untouched slab is the one outcome that cannot " +
                    "have happened.");
                Assert.That(upper.Fragments[0].Activated, Is.True);
                Assert.That(upper.Fragments[1].Activated, Is.True,
                    "Every piece comes away: a budget-denied remainder would hang in the " +
                    "air exactly where the released rigid body used to.");
                Assert.That(upper.IsStructurallyReleased, Is.True);
                Assert.That(node.Released, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(upperObject);
                if (carrier != null) Object.DestroyImmediate(carrier.gameObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void LostStaticFootingStillDropsTheIntactBodyInOnePiece()
        {
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            var groundObject = new GameObject("Static Ledge");
            var propObject = new GameObject("Prop On A Ledge");
            try
            {
                profile.IntactBody = IntactBodyMode.ReleaseWhenUnsupported;
                profile.SupportDetection = SupportDetectionMode.AutomaticBelow;
                profile.SupportProbeDistance = 0.35f;
                profile.SupportProbeGrid = 2;
                profile.RequiredSupportRatio = 0.5f;
                profile.SupportGraceTime = 0f;

                groundObject.transform.position = new Vector3(0f, 2f, 0f);
                var ground = groundObject.AddComponent<BoxCollider>();
                ground.size = new Vector3(4f, 4f, 4f);

                var probeObject = new GameObject("Prop Collider");
                probeObject.transform.SetParent(propObject.transform, false);
                probeObject.transform.position = new Vector3(0f, 4.5f, 0f);
                var probe = probeObject.AddComponent<BoxCollider>();
                probe.size = Vector3.one;
                var propBody = propObject.AddComponent<Rigidbody>();
                propBody.isKinematic = true;
                var prop = propObject.AddComponent<DestructibleRoot>();
                var node = propObject.AddComponent<StructuralSupportNode>();
                prop.Profile = profile;
                prop.StructuralNode = node;
                prop.IntactBody = propBody;
                prop.Fragments = new List<FragmentRecord>();
                node.Root = prop;
                node.IntactBody = propBody;
                node.ProbeCollider = probe;
                node.ProbeColliders = new List<Collider> { probe };

                var piece = new GameObject("Prop Piece");
                piece.transform.SetParent(propObject.transform, false);
                piece.transform.position = new Vector3(0f, 4.5f, 0f);
                var pieceCollider = piece.AddComponent<BoxCollider>();
                var pieceBody = piece.AddComponent<Rigidbody>();
                pieceBody.isKinematic = true;
                prop.Fragments.Add(new FragmentRecord
                {
                    Transform = piece.transform,
                    Collider = pieceCollider,
                    Body = pieceBody,
                    Volume = 1f,
                    Mass = 1f,
                    StructuralGroup = 0,
                    GroundAnchor = true,
                    SupportNeighbours = new List<int>()
                });

                Physics.SyncTransforms();
                node.RebuildSupportBaseline();
                Assert.That(node.InitialSupportUnits, Is.GreaterThan(0));

                MethodInfo updateFoundation = typeof(StructuralSupportNode).GetMethod(
                    "UpdateFoundationGroup", PrivateInstance);
                Assert.That(updateFoundation, Is.Not.Null);

                ground.enabled = false;
                Physics.SyncTransforms();
                Assert.That((bool)updateFoundation.Invoke(node,
                    new object[] { profile }), Is.True);

                Assert.That(prop.State, Is.EqualTo(DestructibleState.Released),
                    "Nothing broke this object; the ledge it stood on was taken away. " +
                    "It falls whole and breaks when it lands.");
                Assert.That(prop.Fragments[0].Activated, Is.False);
                Assert.That(propBody.isKinematic, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(propObject);
                Object.DestroyImmediate(groundObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void SlabLetIntoAWallFaceComesDownWithThatWall()
        {
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            DestructibleRoot wall = null;
            var slabObject = new GameObject("Cantilevered Slab");
            try
            {
                profile.IntactBody = IntactBodyMode.ReleaseWhenUnsupported;
                profile.SupportDetection = SupportDetectionMode.AutomaticBelow;
                profile.SupportProbeDistance = 0.35f;
                profile.SupportProbeGrid = 2;
                profile.RequiredSupportRatio = 0.5f;
                profile.SupportGraceTime = 0f;
                profile.MaxUnsupportedSpan = 0f;
                profile.EnforceActiveBodyBudget = false;

                // The wall stands on the ground and reaches far below the slab.
                wall = CreateCarrierPanel("Wall", new Vector3(0f, 2f, 0f),
                    new Vector3(0.4f, 4f, 4f), profile, out BoxCollider wallCollider);

                // The slab is let into the wall's face and projects out over open air.
                // Nothing is under it anywhere: its only support is the vertical joint,
                // which sits BESIDE its probes rather than beneath them.
                var probeObject = new GameObject("Slab Collider");
                probeObject.transform.SetParent(slabObject.transform, false);
                probeObject.transform.position = new Vector3(2.2f, 3.85f, 0f);
                var probe = probeObject.AddComponent<BoxCollider>();
                probe.size = new Vector3(4f, 0.3f, 2f);
                var slabBody = slabObject.AddComponent<Rigidbody>();
                slabBody.isKinematic = true;
                var slab = slabObject.AddComponent<DestructibleRoot>();
                var node = slabObject.AddComponent<StructuralSupportNode>();
                slab.Profile = profile;
                slab.StructuralNode = node;
                slab.IntactBody = slabBody;
                slab.Fragments = new List<FragmentRecord>();
                node.Root = slab;
                node.IntactBody = slabBody;
                node.ProbeCollider = probe;
                node.ProbeColliders = new List<Collider> { probe };

                for (int half = 0; half < 2; half++)
                {
                    var piece = new GameObject($"Slab Half {half}");
                    piece.transform.SetParent(slabObject.transform, false);
                    piece.transform.position = new Vector3(
                        half == 0 ? 1.2f : 3.2f, 3.85f, 0f);
                    var pieceCollider = piece.AddComponent<BoxCollider>();
                    pieceCollider.size = new Vector3(2f, 0.3f, 2f);
                    var pieceBody = piece.AddComponent<Rigidbody>();
                    pieceBody.isKinematic = true;
                    slab.Fragments.Add(new FragmentRecord
                    {
                        Transform = piece.transform,
                        Collider = pieceCollider,
                        Body = pieceBody,
                        Volume = 1.2f,
                        Mass = 1f,
                        StructuralGroup = 0,
                        GroundAnchor = true,
                        SupportNeighbours = new List<int> { 1 - half }
                    });
                }

                Physics.SyncTransforms();
                node.RebuildSupportBaseline();

                FieldInfo groupInitialField = typeof(StructuralSupportNode).GetField(
                    "m_groupInitialUnits", PrivateInstance);
                MethodInfo update = typeof(StructuralSupportNode).GetMethod(
                    "UpdateStructuralGroups", PrivateInstance);
                Assert.That(groupInitialField, Is.Not.Null);
                Assert.That(update, Is.Not.Null);

                var groupInitial = (List<int>)groupInitialField.GetValue(node);
                Assert.That(groupInitial[0], Is.GreaterThan(0),
                    "A slab held by the face of a wall is supported. The 45 degree cone " +
                    "that guards against cycles inside one object must not erase a joint " +
                    "between two of them.");

                // A hole shot in the wall is not the wall going away. Its Intact visual
                // is swapped for the baked shell, and the material at the joint is still
                // attached, so the slab must stay exactly where it is.
                FieldInfo wallState = typeof(DestructibleRoot).GetField(
                    "m_state", PrivateInstance);
                Assert.That(wallState, Is.Not.Null);
                wallCollider.enabled = false;
                var wallShard = new GameObject("Wall Shard");
                wallShard.transform.SetParent(wall.transform, false);
                wallShard.transform.position = new Vector3(0f, 3.85f, 0f);
                var shardCollider = wallShard.AddComponent<BoxCollider>();
                shardCollider.size = new Vector3(0.4f, 1f, 4f);
                var shardBody = wallShard.AddComponent<Rigidbody>();
                shardBody.isKinematic = true;
                wall.Fragments.Add(new FragmentRecord
                {
                    Transform = wallShard.transform,
                    Collider = shardCollider,
                    Body = shardBody,
                    Volume = 1.6f,
                    Mass = 1f,
                    StructuralGroup = 0,
                    GroundAnchor = false,
                    SupportNeighbours = new List<int>()
                });
                wallState.SetValue(wall, DestructibleState.Fractured);
                Physics.SyncTransforms();
                update.Invoke(node, new object[] { profile });

                Assert.That(slab.State, Is.EqualTo(DestructibleState.Intact),
                    "Damaged is not demolished: material at the joint still holds.");

                // The repeated poll takes the cached probe-to-fragment path. It must
                // preserve the same support answer without walking the shard mesh again.
                update.Invoke(node, new object[] { profile });
                Assert.That(slab.State, Is.EqualTo(DestructibleState.Intact));

                wall.Fragments[0].Activated = true;
                Physics.SyncTransforms();
                update.Invoke(node, new object[] { profile });

                Assert.That(slab.State, Is.EqualTo(DestructibleState.Fractured));
                Assert.That(slab.Fragments[0].Activated, Is.True);
                Assert.That(slab.Fragments[1].Activated, Is.True,
                    "With the wall gone the slab has nothing left holding it anywhere.");
            }
            finally
            {
                Object.DestroyImmediate(slabObject);
                if (wall != null) Object.DestroyImmediate(wall.gameObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void AGroupReleaseThatFreesNothingMustNotLeaveTheElementStanding()
        {
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            DestructibleRoot carrier = null;
            var slabObject = new GameObject("Floor Over A Carrier");
            try
            {
                profile.IntactBody = IntactBodyMode.ReleaseWhenUnsupported;
                profile.SupportDetection = SupportDetectionMode.AutomaticBelow;
                profile.SupportProbeDistance = 0.35f;
                profile.SupportProbeGrid = 2;
                profile.RequiredSupportRatio = 0.5f;
                profile.SupportGraceTime = 0f;
                profile.MaxUnsupportedSpan = 0f;
                profile.EnforceActiveBodyBudget = false;

                carrier = CreateCarrierPanel("Carrier", new Vector3(0f, 2f, 0f),
                    new Vector3(4f, 4f, 4f), profile, out BoxCollider carrierCollider);

                var probeObject = new GameObject("Floor Collider");
                probeObject.transform.SetParent(slabObject.transform, false);
                probeObject.transform.position = new Vector3(0f, 4.15f, 0f);
                var probe = probeObject.AddComponent<BoxCollider>();
                probe.size = new Vector3(4f, 0.3f, 4f);
                var slabBody = slabObject.AddComponent<Rigidbody>();
                slabBody.isKinematic = true;
                var slab = slabObject.AddComponent<DestructibleRoot>();
                var node = slabObject.AddComponent<StructuralSupportNode>();
                slab.Profile = profile;
                slab.StructuralNode = node;
                slab.IntactBody = slabBody;
                slab.Fragments = new List<FragmentRecord>();
                node.Root = slab;
                node.IntactBody = slabBody;
                node.ProbeCollider = probe;
                node.ProbeColliders = new List<Collider> { probe };

                // One probe collider means one monitored group, numbered 0 — but the bake
                // filed this element's fragments under a different index. A release scoped
                // to group 0 then matches no material at all.
                for (int half = 0; half < 2; half++)
                {
                    var piece = new GameObject($"Floor Half {half}");
                    piece.transform.SetParent(slabObject.transform, false);
                    piece.transform.position = new Vector3(
                        half == 0 ? -1f : 1f, 4.15f, 0f);
                    var pieceCollider = piece.AddComponent<BoxCollider>();
                    pieceCollider.size = new Vector3(2f, 0.3f, 4f);
                    var pieceBody = piece.AddComponent<Rigidbody>();
                    pieceBody.isKinematic = true;
                    slab.Fragments.Add(new FragmentRecord
                    {
                        Transform = piece.transform,
                        Collider = pieceCollider,
                        Body = pieceBody,
                        Volume = 2.4f,
                        Mass = 1f,
                        StructuralGroup = 1,
                        GroundAnchor = true,
                        SupportNeighbours = new List<int> { 1 - half }
                    });
                }

                Physics.SyncTransforms();
                node.RebuildSupportBaseline();
                Assert.That(node.InitialSupportUnits, Is.GreaterThan(0));

                MethodInfo update = typeof(StructuralSupportNode).GetMethod(
                    "UpdateStructuralGroups", PrivateInstance);
                Assert.That(update, Is.Not.Null);

                carrierCollider.enabled = false;
                MarkStructurallyReleased(carrier);
                Physics.SyncTransforms();
                update.Invoke(node, new object[] { profile });

                Assert.That(slab.Fragments[0].Activated, Is.True);
                Assert.That(slab.Fragments[1].Activated, Is.True,
                    "A release that frees nothing is not a release. Reporting success " +
                    "struck this element off the monitored list and left it hanging as a " +
                    "kinematic shell with green probes over open air.");
            }
            finally
            {
                Object.DestroyImmediate(slabObject);
                if (carrier != null) Object.DestroyImmediate(carrier.gameObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void FlatSlabWhoseEveryPieceIsAnAnchorFallsOnceItsLedgeIsGone()
        {
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            var ledgeObject = new GameObject("Supporting Ledge");
            var rootObject = new GameObject("Floor Slab");
            try
            {
                profile.IntactBody = IntactBodyMode.ReleaseWhenUnsupported;
                profile.SupportProbeDistance = 0.35f;
                profile.MaxUnsupportedSpan = 0f;
                profile.RequiredSupportRatio = 0f;
                profile.EnforceActiveBodyBudget = false;

                // Only the left end of the slab rests on anything.
                ledgeObject.transform.position = new Vector3(0f, 0.5f, 0f);
                var ledge = ledgeObject.AddComponent<BoxCollider>();
                ledge.size = new Vector3(1f, 1f, 1f);

                var root = rootObject.AddComponent<DestructibleRoot>();
                root.Profile = profile;
                root.Fragments = new List<FragmentRecord>();

                // A floor slab is one course thick, so the bake marks EVERY piece of it as
                // a ground anchor: they all sit at the object's lowest point.
                for (int i = 0; i < 4; i++)
                {
                    var piece = new GameObject($"Slab Piece {i}");
                    piece.transform.SetParent(rootObject.transform, false);
                    piece.transform.position = new Vector3(i, 1.5f, 0f);
                    var collider = piece.AddComponent<BoxCollider>();
                    var body = piece.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    var neighbours = new List<int>();
                    if (i > 0) neighbours.Add(i - 1);
                    if (i < 3) neighbours.Add(i + 1);
                    root.Fragments.Add(new FragmentRecord
                    {
                        Transform = piece.transform,
                        Collider = collider,
                        Body = body,
                        Volume = 1f,
                        Mass = 1f,
                        StructuralGroup = 0,
                        GroundAnchor = true,
                        SupportNeighbours = neighbours
                    });
                }

                FieldInfo state = typeof(DestructibleRoot).GetField(
                    "m_state", PrivateInstance);
                MethodInfo releaseUnsupported = typeof(DestructibleRoot).GetMethod(
                    "ReleaseUnsupportedIslandsNow", PrivateInstance);
                Assert.That(state, Is.Not.Null);
                Assert.That(releaseUnsupported, Is.Not.Null);
                state.SetValue(root, DestructibleState.Fractured);

                Physics.SyncTransforms();
                releaseUnsupported.Invoke(root, new object[] { -1 });
                for (int i = 0; i < 4; i++)
                {
                    Assert.That(root.Fragments[i].Activated, Is.False,
                        "One end still rests on the ledge and the slab is one connected " +
                        "piece, so nothing is stranded yet.");
                }

                ledge.enabled = false;
                Physics.SyncTransforms();
                releaseUnsupported.Invoke(root, new object[] { -1 });

                for (int i = 0; i < 4; i++)
                {
                    Assert.That(root.Fragments[i].Activated, Is.True,
                        "With the ledge gone every piece is standing over open air. A " +
                        "slab cannot hold itself up by being its own ground anchor.");
                }
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
                Object.DestroyImmediate(ledgeObject);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void BakedGroundAnchorStopsSeedingOnceItsCarrierIsDestroyed()
        {
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            DestructibleRoot carrier = null;
            var rootObject = new GameObject("Panel Above A Carrier");
            try
            {
                profile.IntactBody = IntactBodyMode.ReleaseWhenUnsupported;
                profile.SupportProbeDistance = 0.35f;
                profile.MaxUnsupportedSpan = 0f;
                profile.EnforceActiveBodyBudget = false;

                carrier = CreateCarrierPanel("Carrier Slab", new Vector3(0f, 0.5f, 0f),
                    new Vector3(4f, 1f, 4f), profile, out BoxCollider _);

                var root = rootObject.AddComponent<DestructibleRoot>();
                root.Profile = profile;
                root.Fragments = new List<FragmentRecord>();

                for (int level = 0; level < 2; level++)
                {
                    var piece = new GameObject($"Course {level}");
                    piece.transform.SetParent(rootObject.transform, false);
                    piece.transform.position = new Vector3(0f, 1.5f + level, 0f);
                    var collider = piece.AddComponent<BoxCollider>();
                    var body = piece.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    root.Fragments.Add(new FragmentRecord
                    {
                        Transform = piece.transform,
                        Collider = collider,
                        Body = body,
                        Volume = 1f,
                        Mass = 1f,
                        StructuralGroup = 0,
                        GroundAnchor = level == 0,
                        SupportNeighbours = new List<int> { 1 - level }
                    });
                }

                FieldInfo state = typeof(DestructibleRoot).GetField(
                    "m_state", PrivateInstance);
                MethodInfo releaseUnsupported = typeof(DestructibleRoot).GetMethod(
                    "ReleaseUnsupportedIslandsNow", PrivateInstance);
                Assert.That(state, Is.Not.Null);
                Assert.That(releaseUnsupported, Is.Not.Null);
                state.SetValue(root, DestructibleState.Fractured);

                Physics.SyncTransforms();
                releaseUnsupported.Invoke(root, new object[] { -1 });
                Assert.That(root.Fragments[0].Activated, Is.False);
                Assert.That(root.Fragments[1].Activated, Is.False,
                    "While the carrier stands, the baked anchor is a genuine seed.");

                MarkStructurallyReleased(carrier);
                Physics.SyncTransforms();
                releaseUnsupported.Invoke(root, new object[] { -1 });

                Assert.That(root.Fragments[0].Activated, Is.True,
                    "GroundAnchor is bake data. It records that a piece sat at its " +
                    "element's lowest point, never that the floor is still there.");
                Assert.That(root.Fragments[1].Activated, Is.True,
                    "Nothing reaches the ground any more, so nothing stays attached.");
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
                if (carrier != null) Object.DestroyImmediate(carrier.gameObject);
                Object.DestroyImmediate(profile);
            }
        }
    }
}
