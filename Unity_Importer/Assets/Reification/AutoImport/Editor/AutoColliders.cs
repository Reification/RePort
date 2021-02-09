// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using UnityEngine;
using UnityEditor;

namespace Reification {
	/// <summary>
	/// Adds mesh colliders recursively
	/// </summary>
	/// <remarks>
	/// Existing colliders preclude adding mesh colliders
	/// When an object has a Level-of-Detail (LoD) control
	/// mesh colliders will be added ONLY to the lowest LoD.
	/// When an object has an associated RigidBody mesh colliders
	/// will be declared convex.
	/// </remarks>
	public class AutoColliders {
		const string menuItemName = "Reification/Auto Colliders";
		const int menuItemPriority = 25;

		[MenuItem(menuItemName, validate = true, priority = menuItemPriority)]
		private static bool Validate() {
			return Selection.gameObjects.Length > 0;
		}

		[MenuItem(menuItemName, priority = menuItemPriority)]
		private static void Execute() {
			Undo.IncrementCurrentGroup();
			Undo.SetCurrentGroupName("Auto Colliders");

			var selectionList = Selection.gameObjects;
			foreach(var selection in selectionList) ApplyTo(selection);
		}

		public static void ApplyTo(GameObject gameObject) {
			recurseAddColliders(gameObject, false);
		}

		// FIXME: Stop recursion at prefabs - those will be handled separately

		private static void recurseAddColliders(GameObject gameObject, bool hasPhysics) {
			using(var editScope = new EP.EditGameObject(gameObject)) {
				var editObject = editScope.editObject;
				hasPhysics |= editObject.GetComponent<Rigidbody>() != null;
				var group = editObject.GetComponent<LODGroup>();
				if(group) {
					// Recurse at lowest LoD Renderers
					var foundLOD = group.GetLODs();
					if(foundLOD.Length > 0)
						foreach(var renderer in foundLOD[foundLOD.Length - 1].renderers) {
							if(renderer.transform == editObject.transform) AddCollider(editObject, hasPhysics);
							else if(renderer.transform.IsChildOf(editObject.transform)) recurseAddColliders(renderer.gameObject, hasPhysics);
						}
					return;
				}

				// Recurse over all children
				AddCollider(editObject, hasPhysics);
				foreach(var child in editObject.Children()) recurseAddColliders(child, hasPhysics);
			}
		}

		static void AddCollider(GameObject gameObject, bool hasPhysics) {
			// If no mesh is defined skip this GameObject
			var meshFilter = gameObject.GetComponent<MeshFilter>();
			var sharedMesh = meshFilter ? meshFilter.sharedMesh : null;
			if(!sharedMesh) return;

			var colliderTarget = gameObject;
			var lodGroup = gameObject.GetComponentInParent<LODGroup>();
			if(lodGroup) colliderTarget = lodGroup.gameObject;

			// If target already has colliders do not modify
			if(colliderTarget.GetComponentsInChildren<Collider>().Length > 0) return;

			// Add a mesh collider to this object
			var meshCollider = EP.AddComponent<MeshCollider>(colliderTarget);
			meshCollider.sharedMesh = sharedMesh;
			meshCollider.convex = hasPhysics;
		}
	}
}
