// Fix only the new LR06 pilot material. Do not save unrelated dirty assets.
string path = "Assets/MoonEnvironment/Variants/LR06Pilot/Materials/LR06Pilot_2.mat";
var material = AssetDatabase.LoadAssetAtPath<Material>(path);
if (!material || !material.shader || material.shader.name != "Universal Render Pipeline/Lit")
    throw new Exception("The isolated LR06 diffuser material is missing or unexpected.");
var color = material.GetColor("_EmissionColor");
if (Mathf.Abs(color.r - 2f) > .001f || Mathf.Abs(color.g - 1.36f) > .001f || Mathf.Abs(color.b - .76f) > .001f)
    throw new Exception("Diffuser emission color differs from the source; refusing to override it.");
material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
material.EnableKeyword("_EMISSION");
EditorUtility.SetDirty(material);
AssetDatabase.SaveAssetIfDirty(material);
AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
material = AssetDatabase.LoadAssetAtPath<Material>(path);
if (!material || !material.IsKeywordEnabled("_EMISSION") ||
    (material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.AnyEmissive) == 0)
    throw new Exception("URP did not preserve the emission keyword after reimport.");
return new { path, emission = material.GetColor("_EmissionColor").ToString(),
    keyword = material.IsKeywordEnabled("_EMISSION"), giFlags = material.globalIlluminationFlags.ToString() };
