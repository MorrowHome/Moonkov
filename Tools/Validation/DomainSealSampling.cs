// Run with unity command eval_file. Preview actors stay inactive; no gameplay callbacks or clients start.
if (UnityEditor.BuildPipeline.isBuildingPlayer || UnityEditor.EditorApplication.isCompiling)
    throw new System.InvalidOperationException("Wait for the existing Editor operation.");
var beforeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
int beforePreviews = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount;
var report = new System.Text.StringBuilder("{\"passed\":true,\"networkSession\":false,\"rigs\":[");
int rigIndex = 0;
foreach (string name in new[] { "DollSinger", "HatsuneMiku", "Shian" })
{
    var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
    UnityEngine.GameObject stage = null;
    try
    {
        stage = new UnityEngine.GameObject("Inactive domain sampler review");
        stage.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(stage, scene);
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>($"Assets/{name}/Prefabs/{name}Player.prefab");
        var root = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, stage.transform);
        var animator = root.GetComponentInChildren<UnityEngine.Animator>(true);
        animator.runtimeAnimatorController = null;
        animator.Rebind();
        var bones = root.GetComponentsInChildren<UnityEngine.Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
        string path = $"Assets/{name}/Animation/" + (name == "DollSinger" ? "Blender/" : "") + "WeaponGestures.json";
        var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>(path);
        var bakeType = typeof(BlenderGestureValidation).GetNestedType("Bake", System.Reflection.BindingFlags.NonPublic);
        var bake = UnityEngine.JsonUtility.FromJson(asset.text, bakeType);
        var names = (string[])bakeType.GetField("bones").GetValue(bake);
        var motions = (System.Array)bakeType.GetField("motions").GetValue(bake);
        var sampler = new Unity.MP_FPS.DollSinger.DollSingerBakedGestures(animator, asset);
        var pairs = new[] { ("腕.L", "ひじ.L"), ("ひじ.L", "手首.L"), ("腕.R", "ひじ.R"), ("ひじ.R", "手首.R") };
        var lengths = pairs.Select(p => UnityEngine.Vector3.Distance(bones[p.Item1].position, bones[p.Item2].position)).ToArray();
        var scales = names.Select(n => bones[n].localScale).ToArray();
        var eyeObject = new UnityEngine.GameObject("Inactive sampler eye");
        eyeObject.transform.SetParent(stage.transform, false);
        var eye = eyeObject.AddComponent<UnityEngine.Camera>(); eye.enabled = false;
        var leftEye = animator.GetBoneTransform(UnityEngine.HumanBodyBones.LeftEye);
        var rightEye = animator.GetBoneTransform(UnityEngine.HumanBodyBones.RightEye);
        var eyePosition = leftEye && rightEye ? (leftEye.position + rightEye.position) * .5f : bones["頭"].position + UnityEngine.Vector3.up * .08f;
        int samples = 0, fingerChecks = 0;
        float maximumFingerError = 0, maximumLengthError = 0;
        foreach (int weapon in new[] { 6, 7, 9, 10, 12, 13 })
        {
            var motion = motions.GetValue(weapon - 1);
            var type = motion.GetType();
            var neutral = (float[])type.GetField("pose").GetValue(motion);
            float seconds = (float)type.GetField("fireSeconds").GetValue(motion);
            foreach (bool fp in new[] { false, true })
            foreach (float pitch in new[] { -65f, 0f, 65f })
            foreach (string mode in new[] { "pose", "fire", "reload" })
            foreach (float phase in new[] { 0f, .15f, .3f, .6f, 1f })
            foreach (int burst in weapon == 6 && mode == "fire" ? new[] { 0, 1, 2 } : new[] { 0 })
            {
                sampler.RestoreBeforeAnimation();
                var direction = UnityEngine.Quaternion.AngleAxis(pitch, UnityEngine.Vector3.right) * UnityEngine.Vector3.forward;
                eye.transform.SetPositionAndRotation(eyePosition, UnityEngine.Quaternion.LookRotation(direction));
                sampler.Apply(weapon, 1, mode == "fire" ? System.Math.Min(phase, .999f) * seconds : 999,
                    mode == "reload", phase, .01f, bones["上半身2"], direction, .1f, burst, 0, fp ? eye : null);
                for (int i = 0; i < names.Length; i++)
                {
                    var bone = bones[names[i]];
                    if (!float.IsFinite(bone.position.x + bone.position.y + bone.position.z) || UnityEngine.Vector3.Distance(bone.localScale, scales[i]) > .00001f)
                        throw new System.InvalidOperationException(name + " invalid transform or scale: " + names[i]);
                    if (!new[] { "人指", "中指", "薬指", "小指", "親指" }.Any(d => names[i].StartsWith(d))) continue;
                    int p = i * 4;
                    var expected = new UnityEngine.Quaternion(neutral[p], neutral[p + 1], neutral[p + 2], neutral[p + 3]);
                    maximumFingerError = UnityEngine.Mathf.Max(maximumFingerError, UnityEngine.Quaternion.Angle(expected, bone.localRotation));
                    fingerChecks++;
                }
                for (int i = 0; i < pairs.Length; i++)
                    maximumLengthError = UnityEngine.Mathf.Max(maximumLengthError, UnityEngine.Mathf.Abs(lengths[i] - UnityEngine.Vector3.Distance(bones[pairs[i].Item1].position, bones[pairs[i].Item2].position)));
                if (weapon == 13 && pitch == 0 && !fp)
                {
                    var forward = bones["中指１.R"].position - bones["手首.R"].position;
                    if (UnityEngine.Vector3.Angle(forward, UnityEngine.Vector3.up) > 25)
                        throw new System.InvalidOperationException(name + " Nocturne was reoriented using its folded index.");
                }
                samples++;
            }
        }
        if (maximumFingerError > .15f || maximumLengthError > .0001f)
            throw new System.InvalidOperationException(name + " changed finger shape or arm length: " + maximumFingerError + " / " + maximumLengthError);
        if (rigIndex++ > 0) report.Append(',');
        report.Append("{\"rig\":\"").Append(name).Append("\",\"samples\":").Append(samples)
            .Append(",\"fingerChecks\":").Append(fingerChecks).Append(",\"maximumFingerErrorDegrees\":")
            .Append(maximumFingerError.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
            .Append(",\"maximumArmLengthErrorMetres\":").Append(maximumLengthError.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('}');
    }
    finally
    {
        if (stage) UnityEngine.Object.DestroyImmediate(stage);
        UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
    }
}
if (beforeScene != UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle || beforePreviews != UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount)
    throw new System.InvalidOperationException("Sampler review changed or leaked an Editor scene.");
report.Append("]}");
System.IO.Directory.CreateDirectory("SourceData/Animations/DomainSeals");
System.IO.File.WriteAllText("SourceData/Animations/DomainSeals/SamplerReview.json", report.ToString() + "\n");
return report.ToString();
