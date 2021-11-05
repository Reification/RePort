// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Reification {
	/// <summary>
	/// Generates baked lighting for selected scenes
	/// </summary>
	public class AutoLightmaps {
		const string menuItemFastName = "Reification/Fast Lightmaps";
		const int menuItemFastPriority = 35;
		const string menuItemGoodName = "Reification/Good Lightmaps";
		const int menuItemGoodPriority = 36;

		[MenuItem(menuItemFastName, validate = true, priority = menuItemFastPriority)]
		[MenuItem(menuItemGoodName, validate = true, priority = menuItemGoodPriority)]
		static private bool Validate() {
			foreach(var sceneAsset in Selection.objects) if(null == sceneAsset as SceneAsset) return false;
			return true;
		}

		[MenuItem(menuItemFastName, priority = menuItemFastPriority)]
		static private void FastExecute() {
			Execute(LightmapBakeMode.fast);
		}

		[MenuItem(menuItemGoodName, priority = menuItemFastPriority)]
		static private void GoodExecute() {
			Execute(LightmapBakeMode.good);
		}

		static private void Execute(LightmapBakeMode lightmapBakeMode) {
			Undo.IncrementCurrentGroup();
			Undo.SetCurrentGroupName("Bake Lightmaps");

			// If no scenes are selected configure and bake combined lightmap for all open scenes
			var scenePathList = new List<string>();
			if(Selection.objects.Length > 0) {
				foreach(var sceneAsset in Selection.objects) scenePathList.Add(AssetDatabase.GetAssetPath(sceneAsset));
			} else {
				var sceneSetupList = EditorSceneManager.GetSceneManagerSetup();
				foreach(var sceneSetup in sceneSetupList) scenePathList.Add(sceneSetup.path);
			}

			EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
			ApplyTo(lightmapBakeMode, scenePathList.ToArray());
		}

		/// <summary>
		/// Immediate combined bake of scenes
		/// </summary>
		/// <remarks>
		/// Baked data will be in a folder named for and adjacent to the first listed scene.
		/// 
		/// Baked lighting, including bounced lighting, will be referenced between scenes
		/// Light probe data is not partitioned between scenes:
		/// https://docs.unity3d.com/2019.4/Documentation/ScriptReference/Lightmapping.BakeMultipleScenes.html
		/// 
		/// WARNING: Any unsaved changes will be lost when this is called. To prevent this, first call:
		///     EditorSceneManager.SaveOpenScenes();
		/// 
		/// PROBLEM: Unity 2019.4 - If AssetDatabase.StartAssetEditing() has been called or importing is active,
		/// lightmap generation will fail and loop with error:
		/// "Cannot call SetTextureImporterSettings(paths[], settings[]) after StartAssetImporting()"
		/// Which is accompanied by errors:
		/// "Could not access TextureImporter at path"
		/// "Could not read textures from Assets"
		/// "Integrate failed on Write Lighting Data job"
		/// PROBLEM: Calling AssetDatabase.StopAssetEditing() during import puts editor into hung state.
		/// SOLUTION: Enqueue scenes to be baked and wait for importing to complete.
		/// </remarks>
		public static void ApplyTo(LightmapBakeMode lightmapBakeMode, params string[] scenePathList) {
			if(scenePathList.Length == 0) return;

			// FIXME: This can get stuck in an infinite loop!
			// https://issuetracker.unity3d.com/issues/lightmapper-gets-into-an-endless-cycle-of-errors-when-changing-scenes-after-baking-in-previous-scene
			// https://issuetracker.unity3d.com/issues/light-baking-gets-stuck-in-a-infinite-loop-when-unloading-a-light-baked-scene-if-you-have-another-scene-open

			var sceneManagerSetup = EditorSceneManager.GetSceneManagerSetup();
			var giWorkflowMode = Lightmapping.giWorkflowMode;
			try {
				// Close current scene and open all listed scenes
				var sceneList = new Scene[scenePathList.Length];
				for(var s = 0; s < scenePathList.Length; ++s) sceneList[s] = EditorSceneManager.OpenScene(scenePathList[s], s == 0 ? OpenSceneMode.Single : OpenSceneMode.Additive);

				// Configure each scene identically
				foreach(var scene in sceneList) {
					EditorSceneManager.SetActiveScene(scene);
					ConfigureLightmaps(scene, lightmapBakeMode);
					EditorSceneManager.SaveScene(scene);
				}

				// PROBLEM: BakeMultipleScenes is asynchronous and can be interrupted
				// SOLUTION: Open all scenes and call Bake()
				// IMPORTANT: Bake() requires that Lightmapping.giWorkflowMode == Lightmapping.GIWorkflowMode.OnDemand
				// https://docs.unity3d.com/ScriptReference/Lightmapping.Bake.html
				Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;
				EditorSceneManager.SetActiveScene(sceneList[0]);
				Lightmapping.Bake();
				EditorSceneManager.SaveOpenScenes();
			} finally {
				Lightmapping.giWorkflowMode = giWorkflowMode;
				// Validate scene manager setup
				var hasActiveScene = false;
				foreach (var sceneSetup in sceneManagerSetup) hasActiveScene |= sceneSetup.isActive;
				if(hasActiveScene) EditorSceneManager.RestoreSceneManagerSetup(sceneManagerSetup);
			}
		}

		// QUESTION: Should individual objects have different lightmap settings?
		// OPTION: Terrain scale=0 (see hover pop-up), or is non-static with mesh marked as contributing-only
		// OPTION: Scale relative to object size, and keep lower levels of detail increased?

		// TODO: Create utility class to save, load and apply complete lighting configuration.
		// Provide named default accessors for bake modes.
		// TODO: Include Unity's defaults.

		public enum LightmapBakeMode {
			none = 0, // Make no changes
			fast = 1, // Large texels, no gathering or occlusion
			good = 2  // Small texels, gathering and occlusion
		}

		// Expose settings in the Lighting panel to scripted modification 
		public static SerializedObject GetLightmapSettings(Scene scene) {
			if(!scene.isLoaded) {
				EditorSceneManager.SaveOpenScenes();
				EditorSceneManager.LoadScene(scene.path, LoadSceneMode.Single);
			}
			EditorSceneManager.SetActiveScene(scene);

			// https://forum.unity.com/threads/access-lighting-window-properties-in-script.328342/#post-2669345
			// GetLightmapSettings() is declared as a static accessor in LightmapEditorSettings
			var getLightmapSettingsMethod = typeof(LightmapEditorSettings).GetMethod("GetLightmapSettings", BindingFlags.Static | BindingFlags.NonPublic);
			var lightmapSettingsObject = getLightmapSettingsMethod.Invoke(null, null) as Object;
			return new SerializedObject(lightmapSettingsObject);
		}

		/// <summary>
		/// Scene settings for lightmap baking
		/// </summary>
		/// <remarks>
		/// Default member values match Unity scene defaults
		/// when using Real-Time Global Illumination.
		/// </remarks>
		[System.Serializable]
		public class LightmapSettings {
			// TODO: Defaults should match Unity scene defaults
			
			public int atlasSize = 1024; // texels
			public int padding = 2; // texels
			// QUESTION: What determines the optimal padding choice?
			// MISSING: Directional mode
			// LightmapEditorSettings.lightmapsMode = LightmapsMode.CombinedDirectional
			// MISSING: Compress lightmaps
			// MISSING: Compress reflections (will move to AutoReflectionProbes)

			public float resolutionDirect = 40f; // texels / meter
			public float resolutionIndirect = 2f; // texels / meter

			// MISSING: Indirect intensity
			// MISSING: Albedo boost

			public bool ambientOcclusion = false;
			public float ambientOcclusionDistance = 1f; // meters
			public float ambientOcclusionDirect = 0f; // exponent (0 = physical)
			public float ambientOcclusionIndirect = 1f; // exponent (1 = physical)

			// Editor settings Ambient Occlusion direct support:
			// LightmapEditorSettings.enableAmbientOcclusion;
			// LightmapEditorSettings.aoMaxDistance;
			// LightmapEditorSettings.aoExponentIndirect;
			// LightmapEditorSettings.aoExponentDirect;

			// OBSERVATION: LightmapParameters contains additional AmbientOcclusion settings
			// NOTE: Lightmapping.extractAmbientOcclusion ignores exponents, and only works in Enlighten directMode bake:
			// https://docs.unity3d.com/ru/2019.4/ScriptReference/Experimental.Lightmapping-extractAmbientOcclusion.html

			public bool finalGather = false;
			public bool finalGatherDenoising = true;
			public int finalGatherRayCount = 256;

			public LightmapParameters lightmapParameters;
			// TODO: Default should be Unity "Default Medium"
			// MISSING: Path to LightmapParameters asset

			/// <summary>
			/// Get serialized settings from active scene
			/// </summary>
			public static SerializedObject GetSceneSettings() {
				var scene = EditorSceneManager.GetActiveScene();

				// https://forum.unity.com/threads/access-lighting-window-properties-in-script.328342/#post-2669345
				// GetLightmapSettings() is declared as a static accessor in LightmapEditorSettings
				var getLightmapSettingsMethod = typeof(LightmapEditorSettings).GetMethod("GetLightmapSettings", BindingFlags.Static | BindingFlags.NonPublic);
				var lightmapSettingsObject = getLightmapSettingsMethod.Invoke(null, null) as Object;
				return new SerializedObject(lightmapSettingsObject);
			}

			// NOTE: Some lightmap settings can be applied on a per-object basis
			// TODO: Generalize this to modify individual object lightmap settings

			public void GetFromScene() {
				var lightmapSettings = GetSceneSettings();

				atlasSize = lightmapSettings.FindProperty("m_LightmapEditorSettings.m_AtlasSize").intValue;
				padding = lightmapSettings.FindProperty("m_LightmapEditorSettings.m_Padding").intValue;

				resolutionDirect = lightmapSettings.FindProperty("m_LightmapEditorSettings.m_BakeResolution").floatValue;
				resolutionIndirect = lightmapSettings.FindProperty("m_LightmapEditorSettings.m_Resolution").floatValue;

				ambientOcclusion = lightmapSettings.FindProperty("m_LightmapEditorSettings.m_AO").boolValue;
				ambientOcclusionDistance = lightmapSettings.FindProperty("m_LightmapEditorSettings.m_AOMaxDistance").floatValue;
				ambientOcclusionDirect = lightmapSettings.FindProperty("m_LightmapEditorSettings.m_CompAOExponentDirect").floatValue;
				ambientOcclusionIndirect = lightmapSettings.FindProperty("m_LightmapEditorSettings.m_CompAOExponent").floatValue;

				finalGather = lightmapSettings.FindProperty("m_LightmapEditorSettings.m_FinalGather").boolValue;
				finalGatherDenoising = lightmapSettings.FindProperty("m_LightmapEditorSettings.m_FinalGatherFiltering").boolValue;
				finalGatherRayCount = lightmapSettings.FindProperty("m_LightmapEditorSettings.m_FinalGatherRayCount").intValue;

				// QUESTION: Could the parameter fields be directly embedded in the scene instead?
				// https://forum.unity.com/threads/access-lighting-window-properties-in-script.328342/#post-3320725
				// m_LightmapEditorSettings with child field m_LightmapParameters is an object reference to a .giparams file
				lightmapParameters = lightmapSettings.FindProperty("m_LightmapEditorSettings.m_LightmapParameters").objectReferenceValue as LightmapParameters;
			}

			// FIXME: Use active scene
			public void ApplyToScene() {
				var lightmapSettings = GetSceneSettings();

				// Convert scene to use Enlighten
				LightmapEditorSettings.lightmapper = LightmapEditorSettings.Lightmapper.Enlighten;
				Lightmapping.realtimeGI = true;
				Lightmapping.bakedGI = true;
				LightmapEditorSettings.mixedBakeMode = MixedLightingMode.IndirectOnly;

				lightmapSettings.FindProperty("m_LightmapEditorSettings.m_AtlasSize").intValue = Mathf.NextPowerOfTwo(Mathf.Clamp(1024, atlasSize, 4096));
				lightmapSettings.FindProperty("m_LightmapEditorSettings.m_Padding").intValue = padding;
				LightmapEditorSettings.lightmapsMode = LightmapsMode.CombinedDirectional;
				LightmapEditorSettings.textureCompression = false;

				// TODO: Move this to the reflection probes configuration
				// NOTE: Additional settings include Resolution, Intensity & Bounces
				LightmapEditorSettings.reflectionCubemapCompression = UnityEngine.Rendering.ReflectionCubemapCompression.Uncompressed;

				lightmapSettings.FindProperty("m_LightmapEditorSettings.m_BakeResolution").floatValue = resolutionDirect;
				lightmapSettings.FindProperty("m_LightmapEditorSettings.m_Resolution").floatValue = resolutionIndirect;

				lightmapSettings.FindProperty("m_LightmapEditorSettings.m_AO").boolValue = ambientOcclusion;
				lightmapSettings.FindProperty("m_LightmapEditorSettings.m_AOMaxDistance").floatValue = ambientOcclusionDistance;
				lightmapSettings.FindProperty("m_LightmapEditorSettings.m_CompAOExponentDirect").floatValue = ambientOcclusionDirect;
				lightmapSettings.FindProperty("m_LightmapEditorSettings.m_CompAOExponent").floatValue = ambientOcclusionIndirect;
				//lightmapSettings.FindProperty("m_ExtractAmbientOcclusion").boolValue = true; // Create separate texture for ambient occlusion

				lightmapSettings.FindProperty("m_LightmapEditorSettings.m_FinalGather").boolValue = finalGather;
				lightmapSettings.FindProperty("m_LightmapEditorSettings.m_FinalGatherFiltering").boolValue = finalGatherDenoising;
				lightmapSettings.FindProperty("m_LightmapEditorSettings.m_FinalGatherRayCount").intValue = finalGatherRayCount;

				// QUESTION: Could the parameter fields be directly embedded in the scene instead?
				// https://forum.unity.com/threads/access-lighting-window-properties-in-script.328342/#post-3320725
				// m_LightmapEditorSettings with child field m_LightmapParameters is an object reference to a .giparams file
				lightmapSettings.FindProperty("m_LightmapEditorSettings.m_LightmapParameters").objectReferenceValue = lightmapParameters;

				lightmapSettings.ApplyModifiedProperties();
			}

			// TODO: Save serialized settings as asset
		}

		public static LightmapSettings FastSettings() {
			return new LightmapSettings() {
				atlasSize = 1024,
				padding = 2,
				resolutionDirect = 10f,
				resolutionIndirect = 2f,
				ambientOcclusion = true,
				ambientOcclusionDistance = 1f,
				ambientOcclusionDirect = 0f,
				ambientOcclusionIndirect = 1f,
				finalGather = true,
				finalGatherDenoising = true,
				finalGatherRayCount = 128,
				lightmapParameters = AssetDatabase.LoadAssetAtPath<LightmapParameters>(fastParametersPath)
			};
		}

		public static LightmapSettings GoodSettings() {
			return new LightmapSettings() {
				atlasSize = 4096,
				padding = 2,
				resolutionDirect = 50f,
				resolutionIndirect = 10f,
				ambientOcclusion = true,
				ambientOcclusionDistance = 1f,
				ambientOcclusionDirect = 0f,
				ambientOcclusionIndirect = 1f,
				finalGather = true,
				finalGatherDenoising = true,
				finalGatherRayCount = 512,
				lightmapParameters = AssetDatabase.LoadAssetAtPath<LightmapParameters>(goodParametersPath)
			};
		}

		// TODO: Hard-code these LightmapParameters

		public const string fastParametersPath = "Assets/Reification/AutoImport/Settings/FastLightmaps.giparams";

		public static void FastResolution(SerializedObject lightmapSettings) {
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_BakeResolution").floatValue = 10f; // Direct Resolution (texels / meter)
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_Resolution").floatValue = 2f; // Indirect Resolution (texels / meter)
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_Padding").intValue = 2;
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_AtlasSize").intValue = Mathf.NextPowerOfTwo(Mathf.Clamp(1024, 32, 4096));
		}

		public const string goodParametersPath = "Assets/Reification/AutoImport/Settings/GoodLightmaps.giparams";

		public static void GoodResolution(SerializedObject lightmapSettings) {
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_BakeResolution").floatValue = 50f; // Direct Resolution (texels / meter)
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_Resolution").floatValue = 20f; // Indirect Resolution (texels / meter)
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_Padding").intValue = 2;
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_AtlasSize").intValue = Mathf.NextPowerOfTwo(Mathf.Clamp(4096, 32, 4096));
		}

		// Configure scene for lightmap baking
		public static void ConfigureLightmaps(Scene scene, LightmapBakeMode lightmapBakeMode = LightmapBakeMode.none) {
			// TODO: Merge these with the serialized object editing below
			Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;

			LightmapSettings lightmapSettings = null;
			switch(lightmapBakeMode) {
			case LightmapBakeMode.fast:
				lightmapSettings = FastSettings();
				break;
			case LightmapBakeMode.good:
				lightmapSettings = GoodSettings();
				break;
			default:
				Debug.Log($"Unhandled lightmapBakeMode {lightmapBakeMode}");
				return;
			}
			lightmapSettings.ApplyToScene();
		}
	}
}
