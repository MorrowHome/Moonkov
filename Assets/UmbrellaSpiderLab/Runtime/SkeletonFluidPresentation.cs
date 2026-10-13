using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.SpiderLab
{
    /// <summary>
    /// GPU SPH-style fluid: neighbour pressure/cohesion, XSPH viscosity, gravity and
    /// soft attraction toward the moving rig. The rendered surface is the particle
    /// density isosurface, not the source skin. All buffers/textures belong to this instance.
    /// </summary>
    public sealed class SkeletonFluidPresentation
    {
        [StructLayout(LayoutKind.Sequential)] public struct Seed { public Vector4 rest, bones, weights; }
        [StructLayout(LayoutKind.Sequential)] private struct Particle { public Vector3 position; public float pad0; public Vector3 velocity; public float pad1; }
        [Serializable] public struct Sample
        { public int particles, nonFinite; public float meanGuideDistance, maxGuideDistance, meanSpeed, meanBodyDistance, meanAppendageDistance; }
        [StructLayout(LayoutKind.Sequential)] private struct Obstacle
        { public Matrix4x4 toBox, fromBox; public Vector4 halfSize; }
        private const int MaximumObstacles = 96;
        private readonly int m_VolumeSide, m_GridSide;
        private readonly float m_HalfVolume;
        private readonly Transform m_Root;
        private readonly ComputeShader m_Solver;
        private readonly Material m_Material;
        private readonly Mesh m_BoxMesh;
        private readonly ComputeBuffer m_Seeds, m_Heads, m_Next, m_Densities, m_Bones, m_PreviousBones, m_Obstacles;
        private ComputeBuffer m_Read, m_Write;
        private readonly RenderTexture m_Accum, m_VolumeA, m_VolumeB;
        private readonly Matrix4x4[] m_BoneMatrices, m_PreviousMatrices;
        private readonly Collider[] m_Nearby = new Collider[256];
        private readonly Obstacle[] m_Boxes = new Obstacle[MaximumObstacles];
        private readonly int m_Mask;
        private readonly bool m_ChainRig;
        private readonly int k_Initialize, k_Transport, k_ClearGrid, k_BuildGrid, k_Density, k_Integrate,
            k_ClearVolume, k_Deposit, k_ResolveX, k_BlurY, k_BlurZ;
        private Matrix4x4 m_PreviousRoot;
        private bool m_Initialized;
        private readonly Seed[] m_SeedData;
        private readonly FluidFilaments m_Filaments;
        private readonly ComputeBuffer m_LinkIndices, m_LinkLengths;
        private readonly ComputeBuffer m_RestCentres;
        private readonly Vector4[] m_Centres;
        public int FilamentCount => m_Filaments.Count;
        public int ParticleCount { get; }
        public bool AttractionEnabled { get; set; } = true;
        public bool Predatory { get; set; } = true;
        public SpiderFluidSettings Settings { get; set; } = new SpiderFluidSettings();
        public ComputeBuffer Particles => m_Read;
        public RenderTexture DensityVolume => m_VolumeA;
        public Transform Root => m_Root;

        public SkeletonFluidPresentation(Transform parent, int mask)
            : this(parent, mask, ReadUmbraSeeds(), 89, 2.8f, 96, false) { }

        /// <summary>Shared solver for an explicitly supplied weighted rig. Spider defaults stay
        /// unchanged; chain rigs supply their own rest volume, bones and collision radius.</summary>
        public SkeletonFluidPresentation(Transform parent, int mask, Seed[] seeds, int boneCount,
            float halfVolume, int volumeSide, bool chainRig, bool branchedRig = false)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supports3DRenderTextures)
                throw new NotSupportedException("The skeleton fluid variant requires compute shaders and 3D textures.");
            if (seeds == null || seeds.Length < 1 || seeds.Length > 16000 || boneCount < 1 || boneCount > 256)
                throw new ArgumentException("Invalid skeleton fluid budget.");
            m_Mask = mask; m_ChainRig = chainRig && !branchedRig; ParticleCount = seeds.Length;
            m_HalfVolume = Mathf.Max(2.8f, halfVolume); m_VolumeSide = volumeSide;
            m_GridSide = Mathf.CeilToInt(m_HalfVolume * 2 / .13f);
            m_BoneMatrices = new Matrix4x4[boneCount]; m_PreviousMatrices = new Matrix4x4[boneCount];
            m_Centres = new Vector4[boneCount];
            var solver = Resources.Load<ComputeShader>("Umbra08Fluid");
            var shader = Resources.Load<Shader>("Umbra08FluidSurface");
            if (!solver || !shader) throw new InvalidOperationException("Umbra fluid resources are missing.");
            m_Solver = UnityEngine.Object.Instantiate(solver);
            m_Solver.SetFloat("_HalfVolume", m_HalfVolume);
            m_Solver.SetFloat("_CollisionRadius", chainRig ? .025f : .055f);
            m_Solver.SetInt("_GuideKind", branchedRig ? 2 : chainRig ? 1 : 0);
            m_SeedData = seeds;
            m_Seeds = new ComputeBuffer(ParticleCount, 48); m_Seeds.SetData(seeds);
            m_Read = new ComputeBuffer(ParticleCount, 32); m_Write = new ComputeBuffer(ParticleCount, 32);
            m_Heads = new ComputeBuffer(m_GridSide * m_GridSide * m_GridSide, 4);
            m_Next = new ComputeBuffer(ParticleCount, 4); m_Densities = new ComputeBuffer(ParticleCount, 4);
            m_Bones = new ComputeBuffer(boneCount, 64); m_PreviousBones = new ComputeBuffer(boneCount, 64);
            m_RestCentres = new ComputeBuffer(boneCount, 16);
            m_Obstacles = new ComputeBuffer(MaximumObstacles, 144);
            m_Accum = Volume("Fluid particle splats", GraphicsFormat.R32_UInt, m_VolumeSide);
            m_VolumeA = Volume("Fluid density surface", GraphicsFormat.R32_SFloat, m_VolumeSide);
            m_VolumeB = Volume("Fluid density smoothing", GraphicsFormat.R32_SFloat, m_VolumeSide);
            k_Initialize = m_Solver.FindKernel("Initialize"); k_Transport = m_Solver.FindKernel("Transport");
            k_ClearGrid = m_Solver.FindKernel("ClearGrid"); k_BuildGrid = m_Solver.FindKernel("BuildGrid");
            k_Density = m_Solver.FindKernel("Density"); k_Integrate = m_Solver.FindKernel("Integrate");
            k_ClearVolume = m_Solver.FindKernel("ClearVolume"); k_Deposit = m_Solver.FindKernel("Deposit");
            k_ResolveX = m_Solver.FindKernel("ResolveX"); k_BlurY = m_Solver.FindKernel("BlurY"); k_BlurZ = m_Solver.FindKernel("BlurZ");
            m_Solver.SetInt("_ParticleCount", ParticleCount); m_Solver.SetInt("_GridSide", m_GridSide); m_Solver.SetInt("_VolumeSide", m_VolumeSide);
            foreach (int kernel in new[] { k_Initialize, k_Integrate })
            {
                m_Solver.SetBuffer(kernel, "_Seeds", m_Seeds); m_Solver.SetBuffer(kernel, "_Bones", m_Bones);
                m_Solver.SetBuffer(kernel, "_RestCentres", m_RestCentres);
            }
            m_Solver.SetBuffer(k_Integrate, "_PreviousBones", m_PreviousBones);
            m_Solver.SetBuffer(k_Deposit, "_Seeds", m_Seeds);
            m_Solver.SetBuffer(k_Integrate, "_Obstacles", m_Obstacles);
            foreach (int kernel in new[] { k_ClearGrid, k_BuildGrid, k_Density, k_Integrate })
                m_Solver.SetBuffer(kernel, "_Heads", m_Heads);
            foreach (int kernel in new[] { k_BuildGrid, k_Density, k_Integrate })
                m_Solver.SetBuffer(kernel, "_Next", m_Next);
            foreach (int kernel in new[] { k_Density, k_Integrate })
                m_Solver.SetBuffer(kernel, "_Densities", m_Densities);
            foreach (int kernel in new[] { k_ClearVolume, k_Deposit, k_ResolveX })
                m_Solver.SetTexture(kernel, "_Accum", m_Accum);
            m_Solver.SetTexture(k_ResolveX, "_WriteVolume", m_VolumeA);
            m_Solver.SetTexture(k_BlurY, "_ReadVolume", m_VolumeA); m_Solver.SetTexture(k_BlurY, "_WriteVolume", m_VolumeB);
            m_Solver.SetTexture(k_BlurZ, "_ReadVolume", m_VolumeB); m_Solver.SetTexture(k_BlurZ, "_WriteVolume", m_VolumeA);
            m_Root = new GameObject("Umbra 08 / skeleton-attracted particle fluid").transform;
            m_Root.SetParent(parent, false); m_Root.gameObject.layer = 2;
            var restPoints = new Vector3[ParticleCount]; var regions = new int[ParticleCount];
            var segments = new float[ParticleCount];
            for (int i = 0; i < ParticleCount; i++)
            {
                restPoints[i] = m_SeedData[i].rest;
                segments[i] = branchedRig ? m_SeedData[i].bones.z * 16 : chainRig ? (m_SeedData[i].bones.w < 2 ? m_SeedData[i].bones.x * .5f : m_SeedData[i].bones.y) :
                    ((int)m_SeedData[i].bones.y - 1) % 11 + m_SeedData[i].weights.z;
                regions[i] = chainRig || branchedRig ? (int)m_SeedData[i].bones.w :
                    m_SeedData[i].weights.x > .5f ? -1 : ((int)m_SeedData[i].bones.y - 1) / 11;
            }
            m_Filaments = new FluidFilaments(m_Root, restPoints, regions, segments, out var linkIndices, out var linkLengths, m_HalfVolume * 2);
            m_LinkIndices = new ComputeBuffer(ParticleCount, 16); m_LinkIndices.SetData(linkIndices);
            m_LinkLengths = new ComputeBuffer(ParticleCount, 16); m_LinkLengths.SetData(linkLengths);
            m_Solver.SetBuffer(k_Integrate, "_LinkIndices", m_LinkIndices); m_Solver.SetBuffer(k_Integrate, "_LinkLengths", m_LinkLengths);
            m_Material = new Material(shader) { name = "Umbra08 / reconstructed fluid surface" };
            m_Material.SetTexture("_DensityTex", m_VolumeA);
            m_Material.SetFloat("_HalfVolume", m_HalfVolume); m_Material.SetFloat("_VolumeResolution", m_VolumeSide);
            m_BoxMesh = BoxMesh(m_HalfVolume * 2);
            m_Root.gameObject.AddComponent<MeshFilter>().sharedMesh = m_BoxMesh;
            m_Root.gameObject.AddComponent<MeshRenderer>().sharedMaterial = m_Material;
        }

        private static Seed[] ReadUmbraSeeds()
        {
            var asset = Resources.Load<TextAsset>("Umbra08Fluid");
            if (!asset) throw new InvalidOperationException("Umbra fluid seeds are missing.");
            using (var reader = new BinaryReader(new MemoryStream(asset.bytes, false)))
            {
                if (new string(reader.ReadChars(4)) != "UFP1") throw new InvalidDataException("Unsupported fluid seed payload.");
                int count = reader.ReadInt32();
                if (count < 1 || count > 16000) throw new InvalidDataException("Invalid fluid particle budget.");
                var seeds = new Seed[count];
                for (int i = 0; i < count; i++) seeds[i].rest = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), 1);
                for (int i = 0; i < count; i++) seeds[i].bones = new Vector4(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                for (int i = 0; i < count; i++) seeds[i].weights = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                return seeds;
            }
        }

        private static RenderTexture Volume(string name, GraphicsFormat format, int side)
        {
            var descriptor = new RenderTextureDescriptor(side, side)
            {
                dimension = TextureDimension.Tex3D, volumeDepth = side, graphicsFormat = format,
                depthBufferBits = 0, msaaSamples = 1, enableRandomWrite = true
            };
            var texture = new RenderTexture(descriptor) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            if (!texture.Create()) throw new InvalidOperationException("Cannot allocate fluid density volume.");
            return texture;
        }

        private static Mesh BoxMesh(float size)
        {
            // Rendering an implicit surface needs only its bounding box, not one sphere per particle.
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = UnityEngine.Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);
            var points = mesh.vertices;
            for (int i = 0; i < points.Length; i++) points[i] *= size;
            mesh.vertices = points; mesh.RecalculateBounds(); mesh.name = "Fluid volume bounds";
            cube.GetComponent<Collider>().enabled = false; Dispose(cube);
            return mesh;
        }

        public void SetVisible(bool visible)
        {
            if (m_Root.gameObject.activeSelf == visible) return;
            m_Root.gameObject.SetActive(visible);
            // A return from an older look can span arbitrary world travel. Start from the
            // current skeleton instead of transporting stale fluid across that whole distance.
            if (visible) m_Initialized = false;
        }

        public void Draw(LiquidSpiderPresentation guide, SurfaceMotor motor, float dt)
        {
            guide.CopySkinMatrices(m_BoneMatrices, true);
            if (!m_Initialized) guide.CopyRestCentres(m_Centres);
            DrawPrepared(motor.Velocity, dt);
        }

        public void Draw(Matrix4x4[] bones, Vector4[] centres, Vector3 velocity, float dt)
        {
            if (bones.Length != m_BoneMatrices.Length || centres.Length != m_Centres.Length)
                throw new ArgumentException("Fluid guide size changed without rebuilding its seeds.");
            Array.Copy(bones, m_BoneMatrices, bones.Length);
            if (!m_Initialized) Array.Copy(centres, m_Centres, centres.Length);
            DrawPrepared(velocity, dt);
        }

        private void DrawPrepared(Vector3 velocity, float dt)
        {
            ApplySettings();
            Matrix4x4 currentRoot = m_Root.localToWorldMatrix;
            Matrix4x4 transport = m_Root.worldToLocalMatrix * m_PreviousRoot;
            m_Bones.SetData(m_BoneMatrices);
            m_Solver.SetFloat("_Predatory", Predatory ? 1 : 0);
            bool justInitialized = !m_Initialized;
            if (!m_Initialized)
            {
                m_RestCentres.SetData(m_Centres);
                Array.Copy(m_BoneMatrices, m_PreviousMatrices, m_BoneMatrices.Length);
                transport = Matrix4x4.identity;
                m_Solver.SetVector("_InitialVelocity", m_Root.InverseTransformDirection(velocity));
                m_Solver.SetBuffer(k_Initialize, "_Output", m_Read); DispatchParticles(k_Initialize);
                m_Initialized = true;
            }
            else
            {
                m_Solver.SetMatrix("_PreviousToCurrent", transport);
                m_Solver.SetBuffer(k_Transport, "_Input", m_Read); m_Solver.SetBuffer(k_Transport, "_Output", m_Write);
                DispatchParticles(k_Transport); Swap();
            }
            m_PreviousBones.SetData(m_PreviousMatrices);
            m_Solver.SetMatrix("_PreviousToCurrent", transport);
            m_Solver.SetVector("_Gravity", m_Root.InverseTransformDirection(Vector3.down * (Predatory ? Settings.gravity : 9.8f)));
            m_Solver.SetVector("_CarrierVelocity", m_Root.InverseTransformDirection(velocity));
            m_Material.SetFloat("_Predatory", Predatory ? 1 : 0);
            m_Material.SetFloat("_Threshold", Predatory ? Settings.surfaceThreshold : .28f);
            m_Solver.SetFloat("_Attraction", AttractionEnabled ? 1 : 0);
            m_Solver.SetFloat("_FrameDt", Mathf.Max(dt, .001f));
            int obstacles = GatherObstacles();
            if (obstacles > 0) m_Obstacles.SetData(m_Boxes, 0, 0, obstacles);
            m_Solver.SetInt("_ObstacleCount", obstacles);
            float time = Mathf.Clamp(dt, 0, .06f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(time / .004f)); m_Solver.SetFloat("_Step", time / steps);
            for (int step = 0; step < (justInitialized ? 0 : steps); step++)
            {
                m_Solver.Dispatch(k_ClearGrid, (m_GridSide * m_GridSide * m_GridSide + 63) / 64, 1, 1);
                m_Solver.SetBuffer(k_BuildGrid, "_Input", m_Read); DispatchParticles(k_BuildGrid);
                m_Solver.SetBuffer(k_Density, "_Input", m_Read); DispatchParticles(k_Density);
                m_Solver.SetBuffer(k_Integrate, "_Input", m_Read); m_Solver.SetBuffer(k_Integrate, "_Output", m_Write);
                DispatchParticles(k_Integrate); Swap();
            }
            int groups = m_VolumeSide / 4;
            m_Solver.Dispatch(k_ClearVolume, groups, groups, groups);
            m_Solver.SetBuffer(k_Deposit, "_Input", m_Read); DispatchParticles(k_Deposit);
            m_Solver.Dispatch(k_ResolveX, groups, groups, groups);
            m_Solver.Dispatch(k_BlurY, groups, groups, groups); m_Solver.Dispatch(k_BlurZ, groups, groups, groups);
            m_Filaments.Draw(m_Read, m_Root.InverseTransformDirection(Vector3.down),
                m_Root.InverseTransformDirection(velocity), Predatory, Settings, m_VolumeA,
                Predatory ? Settings.surfaceThreshold : .28f, m_HalfVolume, m_VolumeSide);
            Array.Copy(m_BoneMatrices, m_PreviousMatrices, m_BoneMatrices.Length); m_PreviousRoot = currentRoot;
        }

        private void ApplySettings()
        {
            var s = Settings;
            s.ApplyMaterial(m_Material, !Predatory);
            m_Solver.SetVector("_Thickness", new Vector4(s.mantleThickness, s.rootThickness, s.kneeThickness, s.toeThickness));
            m_Solver.SetFloat("_LegAsymmetry", s.legAsymmetry);
            m_Solver.SetFloat("_ThicknessVariation", s.localThicknessVariation);
            m_Solver.SetFloat("_LiquidMass", Mathf.Max(.1f, s.liquidMass));
            m_Solver.SetFloat("_AppendageMass", s.appendageMass);
            m_Solver.SetFloat("_MotionTrail", s.motionTrail);
            m_Solver.SetFloat("_FreeFlowRadius", s.freeFlowRadius);
            m_Solver.SetFloat("_BodyAttraction", s.bodyAttraction);
            m_Solver.SetFloat("_FootAttraction", s.footAttraction);
            m_Solver.SetFloat("_BodyDamping", s.bodyDamping);
            m_Solver.SetFloat("_FootDamping", s.footDamping);
            m_Solver.SetFloat("_Viscosity", s.viscosity);
            m_Solver.SetFloat("_Pressure", s.pressure);
            m_Solver.SetFloat("_Cohesion", s.cohesion);
            m_Solver.SetVector("_AccelerationLimits", new Vector4(s.bodyAccelerationLimit, s.tipAccelerationLimit, s.bodySpeedLimit, s.tipSpeedLimit));
            m_Solver.SetFloat("_BondDensity", s.filamentDensity);
            m_Solver.SetFloat("_BreakLength", s.minimumBreakLength);
            m_Solver.SetFloat("_BreakStretch", s.breakStretch);
            m_Solver.SetFloat("_BondTension", s.bondTension);
            m_Solver.SetFloat("_BondDamping", s.bondDamping);
        }

        private int GatherObstacles()
        {
            int count = Physics.OverlapSphereNonAlloc(m_Root.position, m_HalfVolume + .4f, m_Nearby, m_Mask, QueryTriggerInteraction.Ignore);
            int written = 0;
            for (int i = 0; i < count && written < MaximumObstacles; i++)
            {
                var collider = m_Nearby[i]; Matrix4x4 boxToWorld;
                if (collider is BoxCollider box)
                    boxToWorld = box.transform.localToWorldMatrix * Matrix4x4.TRS(box.center, Quaternion.identity, box.size * .5f);
                else
                {
                    // MC00's authored collision proxies are individual MeshColliders,
                    // including the floor planes. Include those; only combined render meshes
                    // are excluded. Small equipment also receives conservative AABBs.
                    var bounds = collider.bounds;
                    if (!collider.name.StartsWith("COL_", StringComparison.Ordinal) &&
                        (bounds.size.x > 1.6f || bounds.size.z > 1.6f)) continue;
                    var extent = bounds.extents;
                    // A zero-thickness authored plane still needs a nonsingular contact slab.
                    extent.x = Mathf.Max(.015f, extent.x); extent.y = Mathf.Max(.015f, extent.y); extent.z = Mathf.Max(.015f, extent.z);
                    boxToWorld = Matrix4x4.TRS(bounds.center, Quaternion.identity, extent);
                }
                var half = new Vector3(boxToWorld.GetColumn(0).magnitude, boxToWorld.GetColumn(1).magnitude, boxToWorld.GetColumn(2).magnitude);
                if (Mathf.Min(half.x, half.y, half.z) < .001f) continue;
                m_Boxes[written++] = new Obstacle
                {
                    toBox = boxToWorld.inverse * m_Root.localToWorldMatrix,
                    fromBox = m_Root.worldToLocalMatrix * boxToWorld,
                    halfSize = new Vector4(half.x, half.y, half.z, 0)
                };
            }
            return written;
        }

        private void DispatchParticles(int kernel) { m_Solver.Dispatch(kernel, (ParticleCount + 63) / 64, 1, 1); }
        private void Swap() { var buffer = m_Read; m_Read = m_Write; m_Write = buffer; }

        /// <summary>One explicit validation readback; never called by gameplay Update.</summary>
        public Sample SampleParticles()
        {
            var particles = new Particle[ParticleCount]; m_Read.GetData(particles);
            var sample = new Sample { particles = ParticleCount };
            int bodyCount = 0, appendageCount = 0;
            for (int i = 0; i < particles.Length; i++)
            {
                var particle = particles[i];
                if (!float.IsFinite(particle.position.x) || !float.IsFinite(particle.position.y) || !float.IsFinite(particle.position.z) ||
                    !float.IsFinite(particle.velocity.x) || !float.IsFinite(particle.velocity.y) || !float.IsFinite(particle.velocity.z))
                { sample.nonFinite++; continue; }
                var seed = m_SeedData[i]; Vector3 target = Vector3.zero;
                for (int j = 0; j < 4; j++) if (seed.weights[j] > 0) target += (Vector3)(m_BoneMatrices[(int)seed.bones[j]] * seed.rest) * seed.weights[j];
                float distance = Vector3.Distance(target, particle.position);
                sample.meanGuideDistance += distance; sample.maxGuideDistance = Mathf.Max(sample.maxGuideDistance, distance);
                sample.meanSpeed += particle.velocity.magnitude;
                if (m_ChainRig && seed.bones.w < 2) { sample.meanBodyDistance += distance; bodyCount++; }
                else { sample.meanAppendageDistance += distance; appendageCount++; }
            }
            sample.meanBodyDistance /= Mathf.Max(1, bodyCount); sample.meanAppendageDistance /= Mathf.Max(1, appendageCount);
            sample.meanGuideDistance /= ParticleCount; sample.meanSpeed /= ParticleCount; return sample;
        }

        public void Release()
        {
            if (m_Root) m_Root.gameObject.SetActive(false);
            m_Read.Release(); m_Write.Release(); m_Seeds.Release(); m_Heads.Release(); m_Next.Release(); m_Densities.Release();
            m_Bones.Release(); m_PreviousBones.Release(); m_Obstacles.Release();
            m_LinkIndices.Release(); m_LinkLengths.Release(); m_Filaments.Release();
            m_RestCentres.Release();
            foreach (var texture in new[] { m_Accum, m_VolumeA, m_VolumeB }) { texture.Release(); Dispose(texture); }
            Dispose(m_Root.gameObject); Dispose(m_BoxMesh); Dispose(m_Material); Dispose(m_Solver);
        }

        private static void Dispose(UnityEngine.Object obj)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj); else UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
