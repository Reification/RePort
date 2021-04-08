// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using UnityEngine;
using UnityEditor;

namespace Reification {
	/// <summary>
	/// Generates baked lighting charts 
	/// </summary>
	/// <remarks>
	/// Charts append information to the highlest detail meshes.
	/// </remarks>
	public class AutoLightCharts: MonoBehaviour {
		const string menuItemName = "Reification/Auto Light Charts";
		const int menuItemPriority = 31;

		[MenuItem(menuItemName, validate = true, priority = menuItemPriority)]
		private static bool Validate() {
			return Selection.gameObjects.Length > 0;
		}

		[MenuItem(menuItemName, priority = menuItemPriority)]
		private static void Execute() {
			Undo.IncrementCurrentGroup();
			Undo.SetCurrentGroupName("Auto Light Charts");

			var selectionList = Selection.gameObjects;
			foreach(var selection in selectionList) ApplyTo(selection);
		}

		/// <summary>
		/// Generate charts for all child lightmapped meshes
		/// </summary>
		/// <param name="regenerate">Force regeneration of charts when true</param>
		/// <param name="keepUnused">Remove unused charts when true</param>
		public static void ApplyTo(GameObject gameObject, bool regenerate = false, bool keepUnused = false) {
			var meshRendererList = gameObject.GetComponentsInChildren<MeshRenderer>();
			foreach(var meshRenderer in meshRendererList) {
				var sharedMesh = meshRenderer.gameObject.GetComponent<MeshFilter>().sharedMesh;
				if(!sharedMesh) continue;
				if(!regenerate && sharedMesh.uv2.Length > 0) continue;
				if(!RequiresLightmapUVs(meshRenderer)) {
					if(!keepUnused) sharedMesh.uv2 = new Vector2[0];
					continue;
				}

				Unwrapping.GenerateSecondaryUVSet(sharedMesh, unwrapParam);
			}
		}

		/// <summary>
		/// Check if mesh renderer will require a secondary lightmap chart
		/// </summary>
		/// <returns>True when secondary lightmap chart will be required</returns>
		public static bool RequiresLightmapUVs(MeshRenderer meshRenderer) {
			var staticFlags = GameObjectUtility.GetStaticEditorFlags(meshRenderer.gameObject);
			if(!staticFlags.HasFlag(StaticEditorFlags.ContributeGI)) return false;
			if(meshRenderer.receiveGI != ReceiveGI.Lightmaps) return false;
			return true;
		}

		// Default chart creation parameters
		// https://docs.unity3d.com/Manual/LightingGiUvs-GeneratingLightmappingUVs.html
		public static UnwrapParam unwrapParam = new UnwrapParam {
			hardAngle = 88f, // degrees - seam threshold
			packMargin = 4f, // texels - uv island padding
			angleError = 8f, // percentage - maximum angle distortion
			areaError = 15f // percentage - maximum area distortion
		};
	}
}
