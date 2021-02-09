// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Reification {
	public class MeshInvert {
		const string menuItemName = "Reification/Invert %&i";
		const int menuItemPriority = 40;
		const string gameObjectMenuName = "GameObject/Reification/Invert";
		const int gameObjectMenuPriority = 20;

		[MenuItem(gameObjectMenuName, validate = true, priority = gameObjectMenuPriority)]
		[MenuItem(menuItemName, validate = true, priority = menuItemPriority)]
		private static bool Validate() {
			return Selection.gameObjects.Length > 0;
		}

		[MenuItem(gameObjectMenuName, validate = false, priority = gameObjectMenuPriority)]
		[MenuItem(menuItemName, validate = false, priority = menuItemPriority)]
		private static void Execute() {
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
			EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
			Undo.IncrementCurrentGroup();
			Undo.SetCurrentGroupName("Mesh/Invert");

			var meshFilter = gameObject.GetComponent<MeshFilter>();
			if(!meshFilter) return;
			var sharedMesh = meshFilter.sharedMesh;
			if(!sharedMesh) return;
			sharedMesh.Copy(sharedMesh.Inverted());
		}
	}
}
