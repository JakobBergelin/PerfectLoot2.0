using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PerfectLoot.ArtStyleTest.Editor
{
    /// <summary>
    /// Sets up the animated cathedral enemies and the animated player: FBX import settings and clips,
    /// Animator Controllers, prefabs (materials in slots.txt order) and the swap in the cathedral scene.
    /// Safe to run again; prefabs keep their GUIDs.
    /// </summary>
    public static class CathedralAnimationSetup
    {
        const string Root = "Assets/ArtStyleTest";
        const string ScenePath = Root + "/Scenes/ArtStyleTest_Cathedral_Comic.unity";

        static readonly string[] EnemyClips = { "Idle", "Move", "Attack", "Hit", "Death" };
        static readonly string[] PlayerClips = { "Idle", "Move" };

        class Model
        {
            public string Name, Fbx, Slots, Controller, Prefab, OldPrefab, MaterialFolder;
            public string[] Clips;
        }

        static Model Enemy(string name) => new Model
        {
            Name = name + "_Animated",
            Fbx = $"{Root}/Models/ComicCathedral/Animated/{name}_Animated.fbx",
            Slots = $"{Root}/Models/ComicCathedral/Animated/slots.txt",
            Controller = $"{Root}/Animators/ComicCathedral/{name}.controller",
            Prefab = $"{Root}/Prefabs/ComicCathedral/{name}_Animated.prefab",
            OldPrefab = $"{Root}/Prefabs/ComicCathedral/{name}.prefab",
            MaterialFolder = $"{Root}/Materials/ComicCathedral",
            Clips = EnemyClips,
        };

        static Model[] Models => new[]
        {
            Enemy("CT_Revenant"), Enemy("CT_Wraith"), Enemy("CT_BellRinger"),
            new Model
            {
                Name = "PL_Player_Animated",
                Fbx = $"{Root}/Models/Comic/Animated/PL_Player_Animated.fbx",
                Slots = $"{Root}/Models/Comic/Animated/slots.txt",
                Controller = $"{Root}/Animators/Comic/PL_Player.controller",
                Prefab = $"{Root}/Prefabs/Comic/PL_Player_Animated.prefab",
                OldPrefab = $"{Root}/Prefabs/Comic/PL_Player.prefab",
                MaterialFolder = $"{Root}/Materials/Comic",
                Clips = PlayerClips,
            },
        };

        [MenuItem("Perfect Loot/Art Test/Set Up Cathedral Animations")]
        public static void Run()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            AssetDatabase.Refresh();
            var prefabs = new Dictionary<Model, GameObject>();
            foreach (var m in Models)
            {
                var clips = Import(m);
                var controller = BuildController(m, clips);
                prefabs[m] = BuildPrefab(m, controller);
            }
            SwapInScene(prefabs);
            AssetDatabase.SaveAssets();
            Debug.Log("[CathedralAnimationSetup] Done.");
        }

        static Dictionary<string, AnimationClip> Import(Model m)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(m.Fbx);
            if (!importer) throw new FileNotFoundException(m.Fbx);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = true;

            var takes = new List<ModelImporterClipAnimation>();
            foreach (var clip in m.Clips)
            {
                var take = importer.defaultClipAnimations.FirstOrDefault(t => t.takeName.EndsWith("_" + clip));
                if (take == null) throw new System.Exception($"{m.Fbx}: no take ending in _{clip}");
                take.name = clip;
                take.loopTime = clip == "Idle" || clip == "Move";
                takes.Add(take);
            }
            importer.clipAnimations = takes.ToArray();
            importer.SaveAndReimport();

            return AssetDatabase.LoadAllAssetsAtPath(m.Fbx).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .ToDictionary(c => c.name);
        }

        static AnimatorController BuildController(Model m, Dictionary<string, AnimationClip> clips)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(m.Controller));
            AssetDatabase.DeleteAsset(m.Controller);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(m.Controller);
            var sm = controller.layers[0].stateMachine;

            controller.AddParameter("Move", AnimatorControllerParameterType.Bool);
            var idle = sm.AddState("Idle", new Vector3(300, 0));
            idle.motion = clips["Idle"];
            sm.defaultState = idle;
            var move = sm.AddState("Move", new Vector3(300, 120));
            move.motion = clips["Move"];
            Instant(idle, move).AddCondition(AnimatorConditionMode.If, 0, "Move");
            Instant(move, idle).AddCondition(AnimatorConditionMode.IfNot, 0, "Move");

            if (!clips.ContainsKey("Attack")) return controller;

            AnimatorState OneShot(string name, Vector3 pos, params AnimatorState[] from)
            {
                controller.AddParameter(name, AnimatorControllerParameterType.Trigger);
                var state = sm.AddState(name, pos);
                state.motion = clips[name];
                foreach (var s in from) Instant(s, state).AddCondition(AnimatorConditionMode.If, 0, name);
                var back = state.AddTransition(idle);
                back.hasExitTime = true; back.exitTime = 0.9f; back.duration = 0.1f;
                return state;
            }
            var attack = OneShot("Attack", new Vector3(600, 0), idle, move);
            OneShot("Hit", new Vector3(600, 120), idle, move, attack);

            controller.AddParameter("Death", AnimatorControllerParameterType.Trigger);
            var death = sm.AddState("Death", new Vector3(600, 240));
            death.motion = clips["Death"];
            var toDeath = sm.AddAnyStateTransition(death);
            toDeath.hasExitTime = false; toDeath.duration = 0.05f; toDeath.canTransitionToSelf = false;
            toDeath.AddCondition(AnimatorConditionMode.If, 0, "Death");
            return controller;
        }

        static AnimatorStateTransition Instant(AnimatorState from, AnimatorState to)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false; t.duration = 0.1f;
            return t;
        }

        static Material[] SlotMaterials(Model m, string prefix)
        {
            var line = File.ReadAllLines(m.Slots).First(l => l.StartsWith(m.Name + "|"));
            return line.Split('|')[1].Split(',').Select(n =>
            {
                var path = $"{m.MaterialFolder}/{prefix}{n.Trim()}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!mat) throw new FileNotFoundException(path);
                return mat;
            }).ToArray();
        }

        static GameObject BuildPrefab(Model m, AnimatorController controller)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(m.Fbx);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.name = Path.GetFileNameWithoutExtension(m.Prefab);
            go.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials = SlotMaterials(m, "");
            var animator = go.GetComponent<Animator>();
            if (!animator) animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, m.Prefab);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static void SwapInScene(Dictionary<Model, GameObject> prefabs)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Select(t => t.gameObject).ToList();
            foreach (var (m, prefab) in prefabs.Select(p => (p.Key, p.Value)))
            {
                var old = AssetDatabase.LoadAssetAtPath<GameObject>(m.OldPrefab);
                var instances = all.Where(go => go && PrefabUtility.IsOutermostPrefabInstanceRoot(go)
                    && PrefabUtility.GetCorrespondingObjectFromSource(go) == old).ToList();
                foreach (var o in instances)
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    var t = inst.transform;
                    t.SetParent(o.transform.parent, false);
                    t.SetSiblingIndex(o.transform.GetSiblingIndex());
                    t.localPosition = o.transform.localPosition;
                    t.localRotation = o.transform.localRotation;
                    t.localScale = o.transform.localScale;
                    inst.name = o.name;
                    inst.tag = o.tag;
                    inst.layer = o.layer;
                    inst.SetActive(o.activeSelf);
                    var oldMats = o.GetComponentInChildren<Renderer>().sharedMaterials;
                    if (oldMats.Any(mat => mat && mat.name.StartsWith("Elite_")))
                        inst.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials = SlotMaterials(m, "Elite_");
                    Debug.Log($"[CathedralAnimationSetup] {o.name}: {m.OldPrefab} -> {m.Prefab}");
                    Object.DestroyImmediate(o);
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
