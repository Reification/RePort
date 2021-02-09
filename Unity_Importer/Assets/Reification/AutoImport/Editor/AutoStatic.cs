// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using UnityEngine;
using UnityEditor;

namespace Reification {
	/// <summary>
	/// Sets static flags for objects recursively
	/// </summary>
	/// <remarks>
	/// When an object has an associated RigidBody
	/// the object flags will be set to nothing (non-static).
	/// Only Everything and Nothing static flags
	/// will be modified.
	/// </remarks>
	public class AutoStatic {
		const string menuItemName = "Reification/Auto Static";
		const int menuItemPriority = 24;

		[MenuItem(menuItemName, validate = true, priority = menuItemPriority)]
		private static bool Validate() {
			return Selection.gameObjects.Length > 0;
		}

		[MenuItem(menuItemName, priority = menuItemPriority)]
		private static void Execute() {
			Undo.IncrementCurrentGroup();
			Undo.SetCurrentGroupName("Auto Static");

			var selectionList = Selection.gameObjects;
			foreach(var selection in selectionList) ApplyTo(selection);
		}

		public static void ApplyTo(GameObject gameObject, bool forceNoMotion, float minBakedArea) {
			recurseSetStatic(gameObject, false, forceNoMotion, minBakedArea);
		}

		// Object does not contribute to motion buffer
		public static bool _forceNoMotion = false; //true;
																							 // Object area must exceed this in some view to be include in lightmap
		public static float _minBakedArea = 0f; //4f;

		public static void ApplyTo(GameObject gameObject) {
			ApplyTo(gameObject, _forceNoMotion, _minBakedArea);
		}

		private const StaticEditorFlags nothing = (StaticEditorFlags)0;
		private const StaticEditorFlags everything = (StaticEditorFlags)~0;

		// FIXME: Stop recursion at prefabs - those will be handled separately
		// TODO: Allow camera motion vectors
		// TODO: Configure for occlusion
		// TODO: Configure nominal scale in lightmap

		private static void recurseSetStatic(GameObject gameObject, bool hasPhysics, bool forceNoMotion, float minBakedArea) {
			using(var editScope = new EP.EditGameObject(gameObject)) {
				var editObject = editScope.editObject;

				hasPhysics |= editObject.GetComponent<Rigidbody>() != null;

				// Do not modify object if already configured
				var flags = GameObjectUtility.GetStaticEditorFlags(editObject);
				var modify = flags == nothing || flags == everything;

				flags = everything;
				if(hasPhysics) {
					if(modify) GameObjectUtility.SetStaticEditorFlags(editObject, nothing);

					// Set motion vectors
					var meshRenderer = editObject.GetComponent<MeshRenderer>();
					if(meshRenderer) {
						Undo.RecordObject(meshRenderer, "Auto Static");
						meshRenderer.motionVectorGenerationMode = forceNoMotion ? MotionVectorGenerationMode.ForceNoMotion : MotionVectorGenerationMode.Object;
					}

					foreach(var child in editObject.Children()) recurseSetStatic(child, hasPhysics, forceNoMotion, minBakedArea);
					return;
				}

				// Check for mesh
				var meshFilter = editObject.GetComponent<MeshFilter>();
				var sharedMesh = meshFilter ? meshFilter.sharedMesh : null;
				if(!sharedMesh) {
					if(modify) GameObjectUtility.SetStaticEditorFlags(editObject, everything);
					foreach(var child in editObject.Children()) recurseSetStatic(child, hasPhysics, forceNoMotion, minBakedArea);
					return;
				}

				// Include in lightmap according to maximum visible area
				// IMPORTANT: Imported meshes preserve original units, with object local scale applied in transform.
				// PROBLEM: A thin diagonal cylinder will have large bounding boxes
				// but a small maximum area.
				// NOTE: Estimated XSection area versus bounds might be a better distinguisher for occlusion
				// TEMP: ASSUME parents are NOT scaled
				var bounds = sharedMesh.bounds.size;
				for(int i = 0; i < 3; ++i) bounds[i] *= editObject.transform.localScale[i];
				var hasLightmapArea =
					minBakedArea <= bounds.x * bounds.y ||
					minBakedArea <= bounds.x * bounds.z ||
					minBakedArea <= bounds.y * bounds.z;
				if(!hasLightmapArea) {
					flags &= ~(
						StaticEditorFlags.ContributeGI |
						StaticEditorFlags.ReflectionProbeStatic |
						StaticEditorFlags.OccluderStatic
					);
				}

				// TODO: Check transparent materials
				// TODO: Check for reflective materials
				{
					var meshRenderer = editObject.GetComponent<MeshRenderer>();
					if(meshRenderer) {
						Undo.RecordObject(meshRenderer, "Auto Static");
						meshRenderer.motionVectorGenerationMode = forceNoMotion ? MotionVectorGenerationMode.ForceNoMotion : MotionVectorGenerationMode.Camera;
					}
				}

				if(modify) GameObjectUtility.SetStaticEditorFlags(editObject.gameObject, flags);
				foreach(var child in editObject.Children()) recurseSetStatic(child, hasPhysics, forceNoMotion, minBakedArea);
			}
		}
	}
}
