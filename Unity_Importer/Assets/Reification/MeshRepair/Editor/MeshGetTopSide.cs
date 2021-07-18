// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Collections.Generic;
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
			if(!Selection.activeGameObject) return false;

			var meshCollider = Selection.activeGameObject.GetComponentInParent<MeshCollider>();
			return !!meshCollider;
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

			// NOTE: If there is an LODGroup parent it will have a collider for the lowest level of detail,
			// which will shadow rays cast to the selected level of detail.
			var disabledColliders = new List<Collider>();
			var parentColliders = Selection.activeGameObject.GetComponentsInParent<Collider>();
			foreach(var collider in parentColliders) {
				if(collider.gameObject == Selection.activeGameObject) continue;
				if(!collider.enabled) continue;
				collider.enabled = false;
				disabledColliders.Add(collider);
			}
			Collider enabledCollider = Selection.activeGameObject.GetComponent<Collider>();
			if(enabledCollider) {
				if(!enabledCollider.enabled) enabledCollider.enabled = true;
				else enabledCollider = null;
			}

			// NOTE: LODGroup will use the lowest detail collider, but sampling requires the highest
			Selection.activeGameObject = ApplyTo(Selection.activeGameObject);

			if(enabledCollider) enabledCollider.enabled = false;
			foreach(var collider in disabledColliders) collider.enabled = true;
		}

		// TODO: Make this an adjustable parameter
		public static float sampleSize = 0.5f; // (meters) length of equilateral triangle side

		enum HitSide: byte {
			front = 0,
			back = 1
		}

		// TODO: This is really part of a raycast utility set
		struct RaycastHitSide {
			public RaycastHit hit;
			public HitSide side;
		}

		public static float grassHeight = 0.5f;

		static bool UncoveredMesh(Vector3 origin, Vector3 direction, float maxDistance, List<RaycastHit> hitList, int hitPick) {
			// Check for covering object within grass height
			// IMPORTANT: Single-sided-surfaces (e.g. water) will be considered to have infinite depth.

			// Raycast back to get exit hits
			// Combine enter and exit hits into a list sorted by distance
			// Iterate through list counting enter and exit hits by object
			// No object count can go below 0 (that implies starting in the object)
			// If no objects are above 0 the hit is not inside an object.
			// NOTE: Each hit represents a transition, so the query is for the interval before
			// the chosen hit, which requires counting front side hits until the identified hit is reached.
			// NOTE: Hits could include some additional selection (such as static objects only)

			var meshHit = hitList[hitPick];

			// Build the combined hit list using distance from front origin
			var backHitList = new List<RaycastHit>(Physics.RaycastAll(meshHit.point, -direction, meshHit.distance));
			var hitSideList = new List<RaycastHitSide>();
			foreach(var hit in hitList) {
				var sideHit = new RaycastHitSide { hit = hit, side = HitSide.front };
				hitSideList.Add(sideHit);
			}
			foreach(var hit in backHitList) {
				var sideHit = new RaycastHitSide { hit = hit, side = HitSide.back };
				sideHit.hit.distance = meshHit.distance - sideHit.hit.distance;
				hitSideList.Add(sideHit);
			}
			hitSideList.Sort(
				(less, more) =>
				less.hit.distance < more.hit.distance ? -1 :
				less.hit.distance > more.hit.distance ? 1 :
				0
			);

			// Count object hits from origin to hitPick
			// Single-sided objects will be assumed to project in the ray direction
			var hitIndex = 0;
			var solidCount = new Dictionary<Collider, int>();
			for(; hitIndex < hitSideList.Count; ++hitIndex) {
				var hit = hitSideList[hitIndex];
				var collider = hit.hit.collider;
				// ASSUME: hitPick is the first hit on collider
				if(collider == meshHit.collider) break;
				if(!solidCount.ContainsKey(collider)) solidCount.Add(collider, 0);
				solidCount[collider] += hit.side == HitSide.front ? 1 : -1;
				// If count < 0 then raycast begain inside of an object, which is ignored
				if(solidCount[collider] < 0) solidCount[collider] = 0;
			}

			// If solidCount has any entries > 0 then hit is inside of an object
			foreach(var solid in solidCount) {
				if(solid.Value > 0) return false;
			}

			// Even if hit is not inside an object, grass must not poke through objects
			// so verify that the grass height contains no objects
			if(hitIndex > 0 && (meshHit.distance - hitSideList[hitIndex - 1].hit.distance) < grassHeight) return false;

			return true;
		}

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
			var topMesh = meshCollider.GetResample(stepX, stepY, Vector3.up, UncoveredMesh);
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
			return topSide;
		}
	}
}
