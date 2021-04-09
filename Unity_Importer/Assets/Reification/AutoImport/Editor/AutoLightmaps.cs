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
		const int menuItemFastPriority = 34;
		const string menuItemGoodName = "Reification/Good Lightmaps";
		const int menuItemGoodPriority = 35;

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

			var sceneSetup = EditorSceneManager.GetSceneManagerSetup();
			var giWorkflowMode = Lightmapping.giWorkflowMode;
			try {
				// Open all of the scenes
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
				EditorSceneManager.RestoreSceneManagerSetup(sceneSetup);
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

		public static void FastResolution(SerializedObject lightmapSettings) {
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_BakeResolution").floatValue = 10f; // Direct Resolution (texels / meter)
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_Resolution").floatValue = 2f; // Indirect Resolution (texels / meter)
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_Padding").intValue = 2;
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_AtlasSize").intValue = Mathf.NextPowerOfTwo(Mathf.Clamp(1024, 32, 4096));
		}

		public static void GoodResolution(SerializedObject lightmapSettings) {
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_BakeResolution").floatValue = 50f; // Direct Resolution (texels / meter)
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_Resolution").floatValue = 20f; // Indirect Resolution (texels / meter)
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_Padding").intValue = 2;
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_AtlasSize").intValue = Mathf.NextPowerOfTwo(Mathf.Clamp(4096, 32, 4096));
		}

		// Editor settings Ambient Occlusion direct support:
		// LightmapEditorSettings.enableAmbientOcclusion = true;
		// LightmapEditorSettings.aoMaxDistance = 1f;
		// LightmapEditorSettings.aoExponentIndirect = 1f;
		// LightmapEditorSettings.aoExponentDirect = 0f;

		// IMPORTANT: LightmapParameters contains additional AmbientOcclusion settings
		// NOTE: ExtractAmbientOcclusion ignores exponents, and only works in Enlighten directMode bake:
		// https://docs.unity3d.com/ru/2019.4/ScriptReference/Experimental.Lightmapping-extractAmbientOcclusion.html

		public static void FastAmbientOcclusion(SerializedObject lightmapSettings) {
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_AO").boolValue = false; // Ambient Occlusion
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_AOMaxDistance").floatValue = 1f; // Max Distance (meters)
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_CompAOExponent").floatValue = 1f; // Indirect Contribution (contrast)
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_CompAOExponentDirect").floatValue = 0f; // Direct Contribution (unphysical)
			//lightmapSettings.FindProperty("m_ExtractAmbientOcclusion").boolValue = false; // Create separate texture for ambient occlusion
		}

		public static void GoodAmbientOcclusion(SerializedObject lightmapSettings) {
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_AO").boolValue = true; // Ambient Occlusion
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_AOMaxDistance").floatValue = 1f; // Max Distance (meters)
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_CompAOExponent").floatValue = 1f; // Indirect Contribution (contrast)
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_CompAOExponentDirect").floatValue = 0f; // Direct Contribution (unphysical)
			//lightmapSettings.FindProperty("m_ExtractAmbientOcclusion").boolValue = true; // Create separate texture for ambient occlusion
		}

		public static void FastFinalGather(SerializedObject lightmapSettings) {
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_FinalGather").boolValue = false;
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_FinalGatherFiltering").boolValue = false; // Denoising
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_FinalGatherRayCount").intValue = 128;
		}

		public static void GoodFinalGather(SerializedObject lightmapSettings) {
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_FinalGather").boolValue = true;
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_FinalGatherFiltering").boolValue = true; // Denoising
			lightmapSettings.FindProperty("m_LightmapEditorSettings.m_FinalGatherRayCount").intValue = 512;
		}

		public const string fastParametersPath = "Assets/Reification/AutoImport/Settings/FastLightmaps.giparams";
		public const string goodParametersPath = "Assets/Reification/AutoImport/Settings/GoodLightmaps.giparams";

		// Reference Enlighten RTGI parameters
		public static void SetLightmapParameters(SerializedObject lightmapSettings, LightmapParameters lightmapParameters) {
			// QUESTION: Could the parameter fields be directly embedded in the scene instead?
			// https://forum.unity.com/threads/access-lighting-window-properties-in-script.328342/#post-3320725
			// m_LightmapEditorSettings with child field m_LightmapParameters is an object reference to a .giparams file
			SerializedProperty lightmapParametersReference = lightmapSettings.FindProperty("m_LightmapEditorSettings.m_LightmapParameters");
			lightmapParametersReference.objectReferenceValue = lightmapParameters;
		}

		// Configure scene for lightmap baking
		public static void ConfigureLightmaps(Scene scene, LightmapBakeMode lightmapBakeMode = LightmapBakeMode.none) {
			// TODO: Merge these with the serialized object editing below
			Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;
			LightmapEditorSettings.lightmapper = LightmapEditorSettings.Lightmapper.Enlighten;
			LightmapEditorSettings.lightmapsMode = LightmapsMode.CombinedDirectional;
			Lightmapping.realtimeGI = true;
			Lightmapping.bakedGI = true;
			LightmapEditorSettings.mixedBakeMode = MixedLightingMode.IndirectOnly; // Expect no mixed mode lighting

			if(lightmapBakeMode == LightmapBakeMode.none) return;
			LightmapParameters lightmapParameters = null;
			SerializedObject lightmapSettings = GetLightmapSettings(scene);
			switch(lightmapBakeMode) {
			case LightmapBakeMode.fast:
				FastResolution(lightmapSettings);
				FastAmbientOcclusion(lightmapSettings);
				FastFinalGather(lightmapSettings);
				lightmapParameters = AssetDatabase.LoadAssetAtPath<LightmapParameters>(fastParametersPath);
				break;
			case LightmapBakeMode.good:
				GoodResolution(lightmapSettings);
				GoodAmbientOcclusion(lightmapSettings);
				GoodFinalGather(lightmapSettings);
				lightmapParameters = AssetDatabase.LoadAssetAtPath<LightmapParameters>(goodParametersPath);
				break;
			}
			if(lightmapParameters) SetLightmapParameters(lightmapSettings, lightmapParameters);
			else Debug.LogWarning($"Missing asset: {fastParametersPath}");
			lightmapSettings.ApplyModifiedProperties();
		}
	}
}
