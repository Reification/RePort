// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Reification {
  /// <summary>
  /// Split and merge meshes and sub meshes according to materials
  /// </summary>
  /// <remarks>
  /// The result of MeshMaterialize is a new GameObject hierarchy containing
  /// one child for each material, with a child for each level of detail.
  /// 
  /// Merging applies to all submeshes in a GameObject hierarchy.
  /// Prefabs will be ignored.
  /// Each level of detail will be merged separately.
  /// Meshes that are not associated with a LODGroup will be merged into LOD0.
  /// After applying MeshMaterialize the mesh - material association will be one-to-one.
  /// </remarks>
  public class MeshMaterialize {
    const string menuItemName = "Reification/Materialize %&m";
    const int menuItemPriority = 42;
    const string gameObjectMenuName = "GameObject/Reification/Materialize";
    const int gameObjectMenuPriority = 22;

    [MenuItem(menuItemName, validate = true, priority = menuItemPriority)]
    [MenuItem(gameObjectMenuName, validate = true, priority = gameObjectMenuPriority)]
    private static bool Validate() {
      if(!EP.useEditorAction) return false;
      return Selection.gameObjects.Length > 0;
    }

    [MenuItem(menuItemName, validate = false, priority = menuItemPriority)]
    [MenuItem(gameObjectMenuName, validate = false, priority = gameObjectMenuPriority)]
    private static void Execute() {
      if(!EP.useEditorAction) {
        Debug.Log("MeshMaterialize cannot be applied during play");
        return;
      }

      var root = ApplyTo(Selection.gameObjects);

      // TODO: Save all of the meshes somewhere

      // TODO: Need to name & parent the merge
    }

    /// <summary>
    /// Split and merge submeshes into single material MeshRenderer instances
    /// </summary>
    /// <remarks>
    /// Meshes will remain associated with their level of detail.
    /// Meshes that are not associated with a level of detail will be assigned level 0.
    /// Combined mesh origin is world coordinate origin.
    /// Lightmap UV information will be lost, but can be regenerated using Unwrapping.GenerateSecondaryUVSet
    /// </remarks>
    /// <param name="gameObjectList">All child mesh renders that are </param>
    /// <returns></returns>
    public static GameObject ApplyTo(params GameObject[] gameObjectList) {
      // Gather MeshRenderers by LOD
      var lodRendererList = new List<List<MeshRenderer>>();
      foreach(var gameObject in gameObjectList) {
        var meshRendererList = gameObject.GetComponentsInChildren<MeshRenderer>();
        foreach(var meshRenderer in meshRendererList) {
          var lodIndex = GetLODIndex(meshRenderer);
          if(lodIndex < 0) lodIndex = 0;
          while(lodRendererList.Count <= lodIndex) lodRendererList.Add(new List<MeshRenderer>());
          lodRendererList[lodIndex].Add(meshRenderer);
				}
			}

      var materialList = new Dictionary<string, Material>();
      var lodMergeList = new Dictionary<string, List<LOD>>();
      for(int lodIndex = 0; lodIndex < lodRendererList.Count; ++lodIndex) {
        // Gather submeshes by material
        var materialMeshList = new Dictionary<string, List<CombineInstance>>();
        foreach(var meshRenderer in lodRendererList[lodIndex]) {
          var meshFilter = meshRenderer.GetComponent<MeshFilter>();
          if(!meshFilter || !meshFilter.sharedMesh) {
            Debug.LogWarning($"MeshRenderer {meshRenderer.Path()} is missing Mesh");
            continue;
          }

          var sharedMaterials = meshRenderer.sharedMaterials;
          var sharedMesh = meshFilter.sharedMesh;
          for(var subMeshIndex = 0; subMeshIndex < sharedMaterials.Length; ++subMeshIndex) {
            // NOTE: If there are more materials than submeshes the remaining materials are applied to the final submesh,
            // in order, effectively defining a distinct multi-pass shader material.
            // https://docs.unity3d.com/Manual/class-MeshRenderer.html
            // QUESTION: What happens if there are more submeshes than materials? (Does it depend on single / multiple materials?)

            // In case of missing materials, break
            if(subMeshIndex >= sharedMaterials.Length) break;

            // Get Material Id
            // OPTIONS: Name, Path/Name, Asset GUID, InstanceID
            // PROBLEM: Multi-pass materials need to given a unique ID.
            var material = sharedMaterials[subMeshIndex];
            var materialId = material.name;
            if(!materialList.ContainsKey(materialId)) materialList.Add(materialId, material);

            // Convert all meshes to world coordinates
            var combineInstance = new CombineInstance {
              mesh = meshFilter.sharedMesh,
              subMeshIndex = subMeshIndex,
              transform = meshFilter.transform.localToWorldMatrix
            };

            if(!materialMeshList.ContainsKey(materialId)) materialMeshList.Add(materialId, new List<CombineInstance>());
            materialMeshList[materialId].Add(combineInstance);
          }
				}

        foreach(var materialId in materialList.Keys) {
          var gameObject = EP.Instantiate();

          // Create combined mesh
          var sharedMesh = new Mesh();
          sharedMesh.CombineMeshes(materialMeshList[materialId].ToArray(), true, true, false);
          var meshFilter = gameObject.AddComponent<MeshFilter>();
          meshFilter.sharedMesh = sharedMesh;

          // Apply material to mesh
          var material = materialList[materialId];
          var meshRenderer = gameObject.AddComponent<MeshRenderer>();
          meshRenderer.sharedMaterials = new Material[]{ material };

          // Register level of detail
          // NOTE: Merge results in only one MeshRenderer for each level of detail
          gameObject.name = material.name + "_LOD" + lodIndex;
          if(!lodMergeList.ContainsKey(materialId)) lodMergeList.Add(materialId, new List<LOD>());
          lodMergeList[materialId].Add(new LOD{
            renderers = new Renderer[] { meshRenderer },
            screenRelativeTransitionHeight = Mathf.Pow(0.5f, lodMergeList[materialId].Count + 1),
            fadeTransitionWidth = 0.25f
          });
          // TODO: Extract and standardize screen fraction and transition fraction
        }
      }

      // Create LODGroup managers for each merged material
      var rootObject = EP.Instantiate();
      foreach(var materialId in materialList.Keys) {
        var material = materialList[materialId];
        var gameObject = EP.Instantiate();
        EP.SetParent(gameObject.transform, rootObject.transform);
        gameObject.name = material.name;
        var lodGroup = gameObject.AddComponent<LODGroup>();
        var lodList = lodMergeList[materialId].ToArray();
        lodGroup.SetLODs(lodList);
        for(var lodIndex = 0; lodIndex < lodList.Length; ++lodIndex) {
          var meshRenderer = lodList[lodIndex].renderers[0];
          EP.SetParent(meshRenderer.transform, lodGroup.transform);
        }
			}
      return rootObject;
		}

    // NOTE: GetLODIndex can be an extension

    /// <summary>
    /// Find the managing LODGroup index
    /// </summary>
    /// <returns>LOD index, or -1 if not part of LODGroup</returns>
    public static int GetLODIndex(Renderer renderer) {
      // ASSUME: If renderer is in a group it is the first LODGroup in parents
      var lodGroup = renderer.GetComponentInParent<LODGroup>();
      if(!lodGroup) return -1;
      var lodList = lodGroup.GetLODs();
      for(var lodIndex = 0; lodIndex < lodList.Length; ++lodIndex) {
        var lod = lodList[lodIndex];
        foreach(var lodRenderer in lod.renderers) {
          if(renderer == lodRenderer) return lodIndex;
				}
			}
      return -1;
		}
  }
}
