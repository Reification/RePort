// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Reification {
  public class AutoLightmaps {
    const string menuItemName = "Reification/Fast Lightmaps";
    const int menuItemPriority = 32;

    [MenuItem(menuItemName, validate = true, priority = menuItemPriority)]
    static private bool Validate() {
      foreach(var sceneAsset in Selection.objects) if(null == sceneAsset as SceneAsset) return false;
      return true;
    }

    [MenuItem(menuItemName, priority = menuItemPriority)]
    static private void Execute() {
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
      ApplyTo(scenePathList.ToArray());
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
    static public void ApplyTo(params string[] scenePathList) {
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
          ConfigureLightmaps(scene);
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

    // QUESTION: Should lower levels of detail have different lightmap settings?
    // OPTION: Terrain scale=0 (see hover pop-up), or is non-static with mesh marked as contributing-only
    // OPTION: Scale relative to object size, and keep lower levels of detail increased?

    // Expose settings in the Lighting panel to scripted modification 
    static public SerializedObject GetLightmapSettings(Scene scene) {
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

    static public void SetResolution(SerializedObject lightmapSettings) {
      lightmapSettings.FindProperty("m_LightmapEditorSettings.m_BakeResolution").floatValue = 20f; // Direct Resolution
      lightmapSettings.FindProperty("m_LightmapEditorSettings.m_Resolution").floatValue = 2f; // Indirect Resolution
      lightmapSettings.FindProperty("m_LightmapEditorSettings.m_Padding").intValue = 2;
      lightmapSettings.FindProperty("m_LightmapEditorSettings.m_AtlasSize").intValue = Mathf.NextPowerOfTwo(Mathf.Clamp(1024, 32, 4096));
    }

    static public void SetAmbientOcclusion(SerializedObject lightmapSettings) {
      lightmapSettings.FindProperty("m_LightmapEditorSettings.m_AO").boolValue = true;
      lightmapSettings.FindProperty("m_LightmapEditorSettings.m_AOMaxDistance").floatValue = 1f; // Max Distance
      lightmapSettings.FindProperty("m_LightmapEditorSettings.m_CompAOExponent").floatValue = 1f; // Indirect Contribution
      lightmapSettings.FindProperty("m_LightmapEditorSettings.m_CompAOExponentDirect").floatValue = 0f; // Direct Contribution
      //lightmapSettings.FindProperty("m_ExtractAmbientOcclusion").boolValue = false;
      /*
      LightmapEditorSettings.enableAmbientOcclusion = true;
      LightmapEditorSettings.aoMaxDistance = 1f;
      LightmapEditorSettings.aoExponentIndirect = 1f;
      LightmapEditorSettings.aoExponentDirect = 0f;
       */
    }

    static public void SetFinalGather(SerializedObject lightmapSettings) {
      lightmapSettings.FindProperty("m_LightmapEditorSettings.m_FinalGather").boolValue = false;//true;
      lightmapSettings.FindProperty("m_LightmapEditorSettings.m_FinalGatherFiltering").boolValue = true;
      lightmapSettings.FindProperty("m_LightmapEditorSettings.m_FinalGatherRayCount").intValue = 256;
    }

    // Reference Enlighten RTGI parameters
    static public void SetLightmapParameters(SerializedObject lightmapSettings, LightmapParameters lightmapParameters) {
      // QUESTION: Could the parameter fields be directly embedded instead?
      // https://forum.unity.com/threads/access-lighting-window-properties-in-script.328342/#post-3320725
      // m_LightmapEditorSettings with child field m_LightmapParameters is an object reference to a .giparams file
      SerializedProperty lightmapParametersReference = lightmapSettings.FindProperty("m_LightmapEditorSettings.m_LightmapParameters");
      lightmapParametersReference.objectReferenceValue = lightmapParameters;
    }

    public const string fastParametersPath = "Assets/Reification/AutoImport/Settings/FastLightmaps.giparams";

    // TODO: Provide a struct for lightmap parameters, with static fast / good default values.

    // Configure scene for fast lightmap baking
    static public void ConfigureLightmaps(Scene scene) {
      var lightmapParameters = AssetDatabase.LoadAssetAtPath<LightmapParameters>(fastParametersPath);
      if(!lightmapParameters) {
        Debug.LogWarning($"Missing asset: {fastParametersPath}");
        return;
			}

      // TODO: Merge these with the serialized object editing below
      Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;
      LightmapEditorSettings.lightmapper = LightmapEditorSettings.Lightmapper.Enlighten;
      LightmapEditorSettings.lightmapsMode = LightmapsMode.CombinedDirectional;
      Lightmapping.realtimeGI = true;
      Lightmapping.bakedGI = true;
      LightmapEditorSettings.mixedBakeMode = MixedLightingMode.IndirectOnly;

      SerializedObject lightmapSettings = GetLightmapSettings(scene);
      SetResolution(lightmapSettings);
      SetAmbientOcclusion(lightmapSettings);
      SetFinalGather(lightmapSettings);
      SetLightmapParameters(lightmapSettings, lightmapParameters);
      lightmapSettings.ApplyModifiedProperties();
    }
  }
}
