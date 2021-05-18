// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Collections.Generic;
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
		/// Default Secondry UV Set creation parameters
		/// </summary>
		/// <remarks>
		/// https://docs.unity3d.com/Manual/LightingGiUvs-GeneratingLightmappingUVs.html
		/// </remarks>
		public static UnwrapParam unwrapParam = new UnwrapParam {
			hardAngle = 88f, // degrees - seam threshold
			packMargin = 4f, // texels - uv island padding
			angleError = 8f, // percentage - maximum angle distortion
			areaError = 15f // percentage - maximum area distortion
		};

		/// <summary>
		/// Configure all child MeshRenderers of GameObject according to level of detail
		/// </summary>
		/// <remarks>
		/// Remderers that are managed by a LODGroup will be configured according to their level.
		/// Renderers that are independent will be configured according their static flags.
		/// </remarks>
		public static void ApplyTo(GameObject gameObject) {
			using(var editScope = new EP.EditGameObject(gameObject)) {
				var editObject = editScope.editObject;

				var rendererList = new List<Renderer>(editObject.GetComponentsInChildren<Renderer>());
				var lodGroupList = editObject.GetComponentsInChildren<LODGroup>();
				foreach(var lodGroup in lodGroupList) {
					ConfigureLODGroup(lodGroup);

					foreach(var lod in lodGroup.GetLODs())
						foreach(var renderer in lod.renderers)
							rendererList.Remove(renderer);
				}
				foreach(var renderer in rendererList) {
					var meshRenderer = renderer as MeshRenderer;
					if(!meshRenderer) continue;

					var staticFlags = GameObjectUtility.GetStaticEditorFlags(meshRenderer.gameObject);
					if(staticFlags.HasFlag(StaticEditorFlags.ContributeGI)) ConvertToLightCharts(meshRenderer);
					else ConvertToLightProbes(meshRenderer);
				}
			}
		}

		/// <summary>
		/// Configure lighting for all MeshRenderers in LODGroup
		/// </summary>
		/// <param name="regenerate">When true, overwrite existing secondary UVs</param>
		/// <param name="keepUnused">When true, remove unused secondary UVs</param>
		public static void ConfigureLODGroup(LODGroup lodGroup, bool regenerate = false, bool keepUnused = false) {
			// TODO: Check if group is static - if not do not generate charts
			// QUESTION: Should the renderers be checked? The group GameObject itself?

			// Only the first level of detail uses charts
			var lods = lodGroup.GetLODs();
			foreach(var renderer in lods[0].renderers) {
				var meshRenderer = renderer as MeshRenderer;
				if(!meshRenderer) continue;
				ConvertToLightCharts(meshRenderer, regenerate);
			}
			for(var l = 1; l < lods.Length; ++l) {
				foreach(var renderer in lods[1].renderers) {
					var meshRenderer = renderer as MeshRenderer;
					if(!meshRenderer) continue;
					ConvertToLightProbes(meshRenderer, keepUnused);
				}
			}
		}

		// TODO: Make this an extension
		public static Mesh SharedMesh(MeshRenderer meshRenderer) {
			var meshFilter = meshRenderer.gameObject.GetComponent<MeshFilter>();
			if(!meshFilter) return null;
			var sharedMesh = meshFilter.sharedMesh;
			if(!sharedMesh) return null;
			return sharedMesh;
		}

		// IDEA: Combine this with LightProbeProxyVolume generation
		// If object area is too large, use the LightDirect mode
		// Use blended light probes for small objects
		// Use proxy volumes for intermediate sizes

		/// <summary>
		/// Configure MeshRenderer to use baked lighting with secondary UV charts
		/// </summary>
		/// <param name="regenerate">When true, overwrite existing secondary UVs</param>
		public static void ConvertToLightCharts(MeshRenderer meshRenderer, bool regenerate = false) {
			var staticFlags = GameObjectUtility.GetStaticEditorFlags(meshRenderer.gameObject);
			staticFlags |= StaticEditorFlags.ContributeGI;
			GameObjectUtility.SetStaticEditorFlags(meshRenderer.gameObject, staticFlags);
			meshRenderer.receiveGI = ReceiveGI.Lightmaps;
			meshRenderer.scaleInLightmap = 1f;

			meshRenderer.receiveShadows = true;
			meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

			meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;

			var sharedMesh = SharedMesh(meshRenderer);
			if(!sharedMesh) return;
			if(regenerate || sharedMesh.uv2.Length == 0) Unwrapping.GenerateSecondaryUVSet(sharedMesh, unwrapParam);
		}

		/// <summary>
		/// Configure MeshRenderer to use probe lighting
		/// </summary>
		/// <param name="keepUnused">When true, remove unused secondary UVs</param>
		public static void ConvertToLightProbes(MeshRenderer meshRenderer, bool keepUnused = false) {
			var staticFlags = GameObjectUtility.GetStaticEditorFlags(meshRenderer.gameObject);
			staticFlags |= StaticEditorFlags.ContributeGI;
			GameObjectUtility.SetStaticEditorFlags(meshRenderer.gameObject, staticFlags);
			meshRenderer.receiveGI = ReceiveGI.LightProbes;
			meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;

			meshRenderer.receiveShadows = true;
			meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
			// IDEA: Use lowest level of detail as shadow proxy

			meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Simple;

			var sharedMesh = SharedMesh(meshRenderer);
			if(!sharedMesh) return;
			if(!keepUnused && sharedMesh.uv2.Length != 0) sharedMesh.uv2 = new Vector2[0];
		}
	}
}
