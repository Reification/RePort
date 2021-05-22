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

      // TODO: Need to name & parent the merge
    }

    public static GameObject ApplyTo(params GameObject[] gameObjectList) {
      var root = EP.Instantiate();

      // Do not merge from inside of prefabs
      // For all selected GameObjects find all MeshRenderers
      // Find all LOD groups & maintain separation
      // MeshRenderers that are not managed by LODGroup are in LOD0
      // Merge submeshes by matching materials
      // - Maintain relative transform of meshes.
      // - Mesh pivot will be origin.
      // IMPORTANT: Final mesh is rendered using all excess materials.
      // This creates a multi-pass rendering with a fixed order,
      // so it is effectively a distinct multi-pass shader material.

      return root;
		}
  }
}
