// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Reification {
  public class AutoLightmaps {
    const string menuItemName = "Reification/Fast Lightmaps";
    const int menuItemPriority = 31;

    [MenuItem(menuItemName, validate = true, priority = menuItemPriority)]
    static private bool Validate() {
      if(Selection.objects.Length == 0) return false;
      foreach(var scene in Selection.objects) if(null == scene as SceneAsset) return false;
      return true;
    }

    [MenuItem(menuItemName, priority = menuItemPriority)]
    static private void Execute() {
      Undo.IncrementCurrentGroup();
      Undo.SetCurrentGroupName("Bake Lightmaps");
      foreach(var scene in Selection.objects) ApplyTo(AssetDatabase.GetAssetPath(scene));

      // NOTE: BakeMultipleScenes loads and bakes scenes simultaneously
      // so that each scene can contribute to lightmaps in adjacent scenes
      // Here, the scenes are opened successively for configuration and baking.
      // OPTION: Separate interface for enqueued baking? This might be helpful since
      // it could apply without overriding lightmaps.
    }

    static public void ApplyTo(string scenePath) {
      var sceneSetup = EditorSceneManager.GetSceneManagerSetup();
      try {
        // IMPORTANT: Lightmapping.Bake() should be called when only scenes contributing to bake are open
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var giWorkflowMode = Lightmapping.giWorkflowMode;
        Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;
        // IMPORTANT: Bake() should not be called when giWorkflowMode == Iterative

        DisableSceneLights(scene); // TEMP: Imported lights are not scaled to render
        ConfigureLightmaps(scene);

        // Unity 2019.4
        // PROBLEM: If AssetDatabase.StartAssetEditing() has been called lightmap generation will fail and loop with error:
        // "Cannot call SetTextureImporterSettings(paths[], settings[]) after StartAssetImporting()"
        // Which is accompanied by errors:
        // "Could not access TextureImporter at path"
        // "Could not read textures from Assets"
        // "Integrate failed on Write Lighting Data job"
        // PROBLEM: Calling AssetDatabase.StopAssetEditing() during import puts editor into hung state.
        // SOLUTION: Enqueue scenes to be baked and wait for importing to complete.
        // CAUTION: This callback might not work in batch mode!
        // https://forum.unity.com/threads/editorapplication-update-callback-is-not-called-while-run-build-from-command-line.512380/
        Lightmapping.Bake();
        Lightmapping.giWorkflowMode = giWorkflowMode;
        EditorSceneManager.SaveScene(scene);
      } finally {
        EditorSceneManager.RestoreSceneManagerSetup(sceneSetup);
      }
    }

    static public void DisableSceneLights(Scene scene) {
      foreach(var sceneObject in scene.GetRootGameObjects()) {
        foreach(var light in sceneObject.GetComponentsInChildren<Light>()) {
          if(light == RenderSettings.sun) continue;
          light.enabled = false;
        }
      }
    }

    // Expose settings in the Lighting panel to scripted modification 
    static public SerializedObject GetLightmapSettings(Scene scene) {
      if(!scene.isLoaded) {
        EditorSceneManager.SaveOpenScenes();
        EditorSceneManager.LoadScene(scene.path, LoadSceneMode.Single);
      }

      // https://forum.unity.com/threads/access-lighting-window-properties-in-script.328342/#post-2669345
      // GetLightmapSettings() is declared as a static accessor in LightmapEditorSettings
      var getLightmapSettingsMethod = typeof(LightmapEditorSettings).GetMethod("GetLightmapSettings", BindingFlags.Static | BindingFlags.NonPublic);
      var lightmapSettingsObject = getLightmapSettingsMethod.Invoke(null, null) as Object;
      return new SerializedObject(lightmapSettingsObject);
    }

    static public void SetResolution(SerializedObject lightmapSettings) {
      lightmapSettings.FindProperty("m_LightmapEditorSettings.m_BakeResolution").floatValue = 2f; // Inirect Resolution
      lightmapSettings.FindProperty("m_LightmapEditorSettings.m_Resolution").floatValue = 20f; // Direct Resolution
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

    // TODO: Provide a struct for lightmap parameters, with static fast / best default values.

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
