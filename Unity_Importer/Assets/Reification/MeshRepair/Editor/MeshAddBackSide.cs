// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Reification {
	public class MeshAddBackSide {
		const string menuItemName = "Reification/Add Backside %&b";
		const int menuItemPriority = 42;
		const string gameObjectMenuName = "GameObject/Reification/Add Backside";
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
				Debug.Log("MeshBackside cannot be applied during play");
				return;
			}

			// WARNING: Scene cannot be marked dirty during play
			EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
			Undo.IncrementCurrentGroup();
			Undo.SetCurrentGroupName("MeshAddBackSide");

			var selectionList = Selection.gameObjects;
			foreach(var selection in selectionList) SearchAt(selection);
		}

		public static void SearchAt(GameObject gameObject) {
			// Selection in a scene will identify only one detail version of an object
			// However, the transformation should be applied to all versions
			var lodGroup = gameObject.GetComponentInParent<LODGroup>();
			if(lodGroup) gameObject = lodGroup.gameObject;

			// Apply the transformation to all meshes in lineage
			// so that groups of objects can be transformed if needed
			var meshFilterList = gameObject.GetComponentsInChildren<MeshFilter>();
			foreach(var meshFilter in meshFilterList) ApplyTo(meshFilter.gameObject);
		}

		public static void ApplyTo(GameObject gameObject) {
			// WARNING: Attempting to add backsides to meshes during play yields "Invalid AABB" errors
			var meshFilter = gameObject.GetComponent<MeshFilter>();
			if(!meshFilter) return;
			var sharedMesh = meshFilter.sharedMesh;
			if(!sharedMesh) return;

			// FIXME: Check if mesh asset is non-modifiable, and create a copy in that case
			sharedMesh.Copy(sharedMesh.AddBackSide());

			// Replicate materials on backside submeshes
			var meshRenderer = gameObject.GetComponent<MeshRenderer>();
			if(meshRenderer) {
				var materials = meshRenderer.sharedMaterials;
				var materialsLength = materials.Length;
				var materialsBackside = new Material[materialsLength * 2];
				for(int materialsIndex = 0; materialsIndex < materialsLength; ++materialsIndex) {
					materialsBackside[materialsIndex] = materials[materialsIndex];
					materialsBackside[materialsIndex + materialsLength] = materials[materialsIndex];
				}
				meshRenderer.materials = materialsBackside;
			}
		}
	}
}
