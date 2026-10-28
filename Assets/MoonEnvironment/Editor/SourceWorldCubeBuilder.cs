using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>Source-latlong conversion, actual GGX mip filtering and Lambert SH projection.</summary>
    public static class SourceWorldCubeBuilder
    {
        private static ComputeShader program;
        private static float faceYSign;
        private static bool calibrated;
        private static ComputeShader Program => program ? program : (program = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MoonEnvironment/Editor/SourceCubeSampling.compute"));

        private static RenderTexture Faces(int size)
        {
            var texture = new RenderTexture(new RenderTextureDescriptor(size, size)
            {
                graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                depthBufferBits = 0, dimension = TextureDimension.Tex2DArray, volumeDepth = 6,
                enableRandomWrite = true, msaaSamples = 1, useMipMap = false
            });
            texture.Create(); return texture;
        }
        private static void ReadFaces(RenderTexture faces, Cubemap cube, int mip)
        {
            var request = AsyncGPUReadback.Request(faces, 0); request.WaitForCompletion();
            if (request.hasError) throw new InvalidOperationException("Source cubemap GPU readback failed.");
            for (int face = 0; face < 6; face++) cube.SetPixelData(request.GetData<ushort>(face), mip, (CubemapFace)face);
        }
        private static Cubemap Convert(Texture2D panorama, int size, bool calibration, float sign)
        {
            var cube = new Cubemap(size, TextureFormat.RGBAHalf, true);
            var faces = Faces(size);
            try
            {
                int kernel = Program.FindKernel("ConvertLatLong");
                Program.SetInt("_Size", size); Program.SetInt("_Calibration", calibration ? 1 : 0); Program.SetFloat("_FaceYSign", sign);
                Program.SetTexture(kernel, "_Panorama", panorama ? panorama : Texture2D.whiteTexture);
                Program.SetTexture(kernel, "_Faces", faces);
                Program.Dispatch(kernel, Mathf.CeilToInt(size / 8f), Mathf.CeilToInt(size / 8f), 6);
                ReadFaces(faces, cube, 0); cube.Apply(false, false);
                return cube;
            }
            catch { UnityEngine.Object.DestroyImmediate(cube); throw; }
            finally { faces.Release(); UnityEngine.Object.DestroyImmediate(faces); }
        }
        private static float TestOrientation(float sign)
        {
            var cube = Convert(null, 32, true, sign);
            var directions = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back,
                new Vector3(.4f,.7f,.3f).normalized, new Vector3(-.7f,-.2f,.8f).normalized,
                new Vector3(.2f,-.8f,-.3f).normalized, new Vector3(-.4f,.3f,-.9f).normalized };
            var input = new Vector4[directions.Length]; var output = new Vector4[directions.Length];
            for (int i = 0; i < input.Length; i++) input[i] = directions[i];
            using var source = new ComputeBuffer(input.Length, 16); using var destination = new ComputeBuffer(output.Length, 16);
            try
            {
                source.SetData(input); int kernel = Program.FindKernel("SampleCube");
                Program.SetInt("_DirectionCount", input.Length); Program.SetTexture(kernel, "_Cube", cube); Program.SetBuffer(kernel, "_Directions", source); Program.SetBuffer(kernel, "_Samples", destination);
                Program.Dispatch(kernel, Mathf.CeilToInt(input.Length / 16f), 1, 1); destination.GetData(output);
                float error = 0;
                for (int i = 0; i < input.Length; i++)
                    for (int c = 0; c < 3; c++) error = Mathf.Max(error, Mathf.Abs(output[i][c] - (.5f + .5f * directions[i][c])));
                return error;
            }
            finally { UnityEngine.Object.DestroyImmediate(cube); }
        }
        [MenuItem("Tools/Moon Environment/Source World/Validate reflection conversion")]
        public static void ValidateConversion()
        {
            Calibrate(); ValidateUniformProjection();
            Directory.CreateDirectory("LocalData/SourceFidelity20261028");
            File.WriteAllText("LocalData/SourceFidelity20261028/ReflectionConversion.txt", "Cubemap axes calibrated. Uniform source radiance projects to unit Lambert irradiance. Face Y sign=" + faceYSign);
        }
        public static void Calibrate()
        {
            if (calibrated) return;
            float positive = TestOrientation(1), negative = TestOrientation(-1);
            faceYSign = positive < negative ? 1 : -1;
            if (Mathf.Min(positive, negative) > .025f) throw new InvalidOperationException("Source cubemap axis calibration failed: " + positive + ", " + negative);
            calibrated = true;
        }
        public static Cubemap Build(Texture2D panorama, int size = 512)
        {
            Calibrate(); var cube = Convert(panorama, size, false, faceYSign);
            try
            {
                int kernel = Program.FindKernel("Prefilter"); Program.SetTexture(kernel, "_Cube", cube);
                Program.SetInt("_SampleCount", 64); Program.SetFloat("_FaceYSign", faceYSign);
                // URP maps perceptual roughness to six specular LOD steps, with
                // r*(1.7-.7*r). Invert that mapping for the prefilter's roughness.
                for (int mip = 1; mip < cube.mipmapCount; mip++)
                {
                    int dimension = Mathf.Max(1, size >> mip); var faces = Faces(dimension);
                    try
                    {
                        float normalized = Mathf.Min(mip / 6f, 1);
                        float roughness = (1.7f - Mathf.Sqrt(2.89f - 2.8f * normalized)) / 1.4f;
                        Program.SetInt("_Size", dimension); Program.SetFloat("_Roughness", roughness); Program.SetTexture(kernel, "_Faces", faces);
                        Program.Dispatch(kernel, Mathf.CeilToInt(dimension / 8f), Mathf.CeilToInt(dimension / 8f), 6);
                        ReadFaces(faces, cube, mip);
                    }
                    finally { faces.Release(); UnityEngine.Object.DestroyImmediate(faces); }
                }
                cube.Apply(false, false);
                EditorUtility.CompressCubemapTexture(cube, TextureFormat.BC6H, TextureCompressionQuality.Best);
                cube.Apply(false, false); return cube;
            }
            catch { UnityEngine.Object.DestroyImmediate(cube); throw; }
        }
        public static void Project(Texture2D panorama, Vector3 towardSun, float sunAngle, out SphericalHarmonicsL2 full, out SphericalHarmonicsL2 masked)
        {
            int width = panorama.width, height = panorama.height;
            if ((width & 7) != 0 || (height & 7) != 0) throw new InvalidOperationException("Source radiance dimensions must be divisible by eight.");
            int columns = width / 8, rows = height / 8, count = columns * rows;
            using var a = new ComputeBuffer(count, 16); using var b = new ComputeBuffer(count, 16);
            int kernel = Program.FindKernel("ProjectTiles");
            Program.SetTexture(kernel, "_Panorama", panorama); Program.SetInt("_PanoramaWidth", width); Program.SetInt("_PanoramaHeight", height);
            Program.SetVector("_SunDirection", towardSun); Program.SetFloat("_SunCosHalfAngle", Mathf.Cos(sunAngle * .5f + Mathf.PI / height));
            Program.SetBuffer(kernel, "_TileFull", a); Program.SetBuffer(kernel, "_TileMasked", b); Program.Dispatch(kernel, columns, rows, 1);
            var first = new Vector4[count]; var second = new Vector4[count]; a.GetData(first); b.GetData(second);
            full = new SphericalHarmonicsL2(); masked = new SphericalHarmonicsL2();
            // AddDirectionalLight normalizes its truncated lobe for a peak of two.
            // Calibrate its constant term to the physical Lambert coefficient 1/4.
            var unit = new SphericalHarmonicsL2(); unit.AddDirectionalLight(Vector3.up, Color.white, 1);
            float nativeToLambert = .25f / unit[0, 0];
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    float longitude = ((x + .5f) / columns - .5f) * 2 * Mathf.PI;
                    float latitude = ((y + .5f) / rows - .5f) * Mathf.PI;
                    var direction = new Vector3(Mathf.Cos(latitude) * Mathf.Sin(longitude), Mathf.Sin(latitude), Mathf.Cos(latitude) * Mathf.Cos(longitude));
                    var va = first[y * columns + x]; var vb = second[y * columns + x];
                    full.AddDirectionalLight(direction, new Color(va.x, va.y, va.z), nativeToLambert);
                    masked.AddDirectionalLight(direction, new Color(vb.x, vb.y, vb.z), nativeToLambert);
                }
        }
        public static void ValidateUniformProjection()
        {
            var image = new Texture2D(256, 128, TextureFormat.RGBAHalf, false, true); var colors = new Color[256 * 128];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            image.SetPixels(colors); image.Apply();
            try
            {
                Project(image, Vector3.up, 0, out var full, out var masked);
                var directions = new[] { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
                var evaluated = new Color[directions.Length]; full.Evaluate(directions, evaluated);
                foreach (var value in evaluated)
                    if (Mathf.Abs(value.r - 1) > .01f || Mathf.Abs(value.g - 1) > .01f || Mathf.Abs(value.b - 1) > .01f)
                        throw new InvalidOperationException("Source radiance/irradiance normalization failed: " + value);
            }
            finally { UnityEngine.Object.DestroyImmediate(image); }
        }
        public static float[] Pack(SphericalHarmonicsL2 value)
        {
            var result = new float[27]; for (int c = 0; c < 3; c++) for (int i = 0; i < 9; i++) result[c * 9 + i] = value[c, i]; return result;
        }
        public static SphericalHarmonicsL2 Unpack(float[] value)
        {
            var result = new SphericalHarmonicsL2(); for (int c = 0; c < 3; c++) for (int i = 0; i < 9; i++) result[c, i] = value[c * 9 + i]; return result;
        }
    }
}
