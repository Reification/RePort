﻿// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Reification {
  public class MeshGetTopSide {
    const string menuItemName = "Reification/Get Topside %&t";
    const int menuItemPriority = 43;
    const string gameObjectMenuName = "GameObject/Reification/Get Topside";
    const int gameObjectMenuPriority = 23;

		// Menu action creates a visible object & saves the mesh, then new object is selected

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
				Debug.Log("MeshGetTopSide cannot be applied during play");
				return;
			}

			// WARNING: Scene cannot be marked dirty during play
			EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
			Undo.IncrementCurrentGroup();
			Undo.SetCurrentGroupName("MeshGetTopSide");

			foreach(var gameObject in Selection.gameObjects) ApplyTo(gameObject);
		}

		// TODO: Make this an adjustable parameter
		public static float sampleSize = 2f; // (meters) length of equilateral triangle side

		public static GameObject ApplyTo(GameObject gameObject) {
			var meshCollider = gameObject.GetComponent<MeshCollider>();
			var hasMeshCollider = !!meshCollider;
			if(!hasMeshCollider) {
				var filter = gameObject.GetComponent<MeshFilter>();
				if(!filter) return null;
				if(!filter.sharedMesh) return null;
				meshCollider = EP.AddComponent<MeshCollider>(gameObject);
				meshCollider.sharedMesh = filter.sharedMesh;
			}
			if(!meshCollider.sharedMesh) return null;

			// Create a top-side mesh
			/*
			var localTop = gameObject.transform.InverseTransformDirection(Vector3.up);
			var topMesh = sharedMesh.GetSingleSide(localTop);
			*/
			var stepX = Vector3.right * sampleSize;
			var stepY = Quaternion.Euler(0f, -60f, 0f) * stepX;
			var topMesh = meshCollider.GetResample(stepX, stepY, Vector3.up);
			topMesh.name = meshCollider.sharedMesh.name + " top";

			// Save mesh asset
			var topMeshPath = AssetDatabase.GetAssetPath(meshCollider.sharedMesh);
			if(topMeshPath == null || !topMeshPath.StartsWith("Assets/")) topMeshPath = EditorSceneManager.GetActiveScene().path;
			topMeshPath = topMeshPath.Substring(0, topMeshPath.LastIndexOf('/')) + "/" + topMesh.name + ".asset";
			AssetDatabase.CreateAsset(topMesh, topMeshPath);

			// Create sibling game object
			var topSide = EP.Instantiate();
			topSide.name = gameObject.name + " top";
			EP.SetParent(topSide.transform, gameObject.transform.parent);
			topSide.transform.localPosition = gameObject.transform.localPosition;
			topSide.transform.localRotation = gameObject.transform.localRotation;
			topSide.transform.localScale = gameObject.transform.localScale;

			// Reference the topside mesh
			var meshFilter = topSide.AddComponent<MeshFilter>();
			meshFilter.sharedMesh = topMesh;

			// Make the new object visible
			// NOTE: The UVs are preserved, so textures should match
			var meshRenderer = gameObject.GetComponent<MeshRenderer>();
			if(meshRenderer) {
				var materials = meshRenderer.sharedMaterials;
				meshRenderer = topSide.AddComponent<MeshRenderer>();
				meshRenderer.sharedMaterials = materials;
			}

			if(!hasMeshCollider) EP.Destroy(meshCollider);

			Selection.activeGameObject = topSide;
			return topSide;
		}
	}
}
