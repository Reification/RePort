// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Reification {
	/// <summary>
	/// Group detail levels of model constituents and modify materials to interpolate
	/// </summary>
	/// <remarks>
	/// This modifies the model by removing any repeated levels of detail.
	/// Ihis modifies associated materials to enable fading between detal levels.
	/// 
	/// Applying this to an existing model will update all LODGroup components and all materials.
	/// Applying this to a model with an added detail level will merge those detail components
	/// into existing LODGroups.
	/// 
	/// Non MeshRenderers are supported, provided they have a sibling MeshFilter 
	/// with a vertex count > 0 determining the relative detail.
	/// </remarks>
	public class AutoLOD {
		const string menuItemName = "Reification/Auto LOD";
		const int menuItemPriority = 23;

		[MenuItem(menuItemName, validate = true, priority = menuItemPriority)]
		static private bool Validate() {
			return Selection.gameObjects.Length > 0;
		}

		[MenuItem(menuItemName, priority = menuItemPriority)]
		static private void Execute() {
			Undo.IncrementCurrentGroup();
			Undo.SetCurrentGroupName("Auto LOD");

			foreach(var gameObject in Selection.gameObjects) {
				ApplyTo(gameObject);
			}
		}

		public static void ApplyTo(GameObject gameObject) {
			using(var editScope = new EP.EditGameObject(gameObject)) {
				var editObject = editScope.editObject;
				// Gather all MeshRenderer components that are leaf nodes
				// NOTE: Prefab components will be managed by applying AutoLOD to the prefab
				var children = editObject.Children(true);
				var groups = new Dictionary<PathName, List<GameObject>>();
				foreach(var child in children) {
					// Only gather leaf node renderers
					if(child.transform.childCount > 0) continue;
					// AutoLOD will be applied to child prefabs separately
					var childPrefab = PrefabUtility.GetNearestPrefabInstanceRoot(child);
					if(childPrefab != null && childPrefab != editObject) continue;
					var hasRenderer = child.GetComponent<Renderer>();
					if(!hasRenderer) continue;
					// MeshRenderers will be managed by LODGroup
					var inGroup = child.GetComponentInParent<LODGroup>();
					if(inGroup) continue;
					// Add renderer to group
					var path = new PathName(child);
					if(!groups.ContainsKey(path)) groups.Add(path, new List<GameObject>());
					groups[path].Add(child);
				}

				// Combine group renderers under LODGroup managers
				foreach(var pathGroup in groups) MergeGroup(pathGroup.Key, pathGroup.Value);
			}
		}

		class RendererSort {
			public Renderer renderer;
			public int vertexCount;
			public RendererSort(Renderer renderer) {
				this.renderer = renderer;
				vertexCount = 0;
				var meshFilter = renderer.GetComponent<MeshFilter>();
				if(meshFilter) {
					vertexCount = meshFilter.sharedMesh?.vertexCount ?? 0;
				}
			}
		}

		public const string lodSuffix = "_LOD";

		static void MergeGroup(string pathName, List<GameObject> group) {
			// Gather LODGroup and Renderers
			LODGroup lodGroup = null;
			var renderers = new List<RendererSort>();
			foreach(var gameObject in group) {
				var renderer = gameObject.GetComponent<Renderer>();
				if(renderer) renderers.Add(new RendererSort(renderer));
				var isGroup = gameObject.GetComponent<LODGroup>();
				if(isGroup) {
					var lods = isGroup.GetLODs();
					foreach(var lod in lods) {
						foreach(var lodRenderer in lod.renderers) {
							// Renderers must begin as siblings of LODGroup
							EP.SetParent(lodRenderer.transform, isGroup.transform.parent);
							renderers.Add(new RendererSort(lodRenderer));
						}
					}

					if(!!lodGroup || !!renderer) {
						// LODGroup manager cannot be duplicated, and cannot have renderer component
						Debug.LogWarning($"Removing LODGroup found on {gameObject.Path()}");
						EP.Destroy(isGroup);
						continue;
					}
					lodGroup = isGroup;
				}
			}
			if(!lodGroup) lodGroup = EP.AddComponent<LODGroup>(EP.Instantiate());

			// renderers[0] has the lowest vertex count
			renderers.Sort((l, m) => l.vertexCount - m.vertexCount);
			// Remove missing meshes and duplicate levels of detail
			var vertexCount = 0;
			var removeRenderers = new List<RendererSort>();
			foreach(var renderer in renderers) {
				if(vertexCount == renderer.vertexCount) {
					removeRenderers.Add(renderer);
					continue;
				}
				vertexCount = renderer.vertexCount;
			}
			foreach(var renderer in removeRenderers) {
				renderers.Remove(renderer);
				EP.Destroy(renderer.renderer.gameObject);
				// NOTE: Duplicate mesh asset could be removed
			}
			if(renderers.Count == 0) {
				EP.Destroy(lodGroup.gameObject);
				return;
			}
			// renderers[0] has the highest vertrex count
			renderers.Reverse();

			// Configure manager in hierarchy
			lodGroup.gameObject.name = pathName.Substring(pathName.LastIndexOf('/') + 1);
			EP.SetParent(lodGroup.transform, renderers[0].renderer.transform.parent);
			lodGroup.transform.localPosition = renderers[0].renderer.transform.localPosition;
			lodGroup.transform.localRotation = renderers[0].renderer.transform.localRotation;
			lodGroup.transform.localScale = renderers[0].renderer.transform.localScale;
			for(var r = 0; r < renderers.Count; ++r) {
				var renderer = renderers[r].renderer;
				// TODO: Used PathNameExtension for this!
				var lodIndex = renderer.gameObject.name.LastIndexOf(lodSuffix);
				if(lodIndex >= 0) renderer.gameObject.name = renderer.gameObject.name.Substring(0, lodIndex);
				renderer.gameObject.name += lodSuffix + r.ToString();
				EP.SetParent(renderer.transform, lodGroup.transform);
			}

			// Configure the group
			var lodList = new LOD[renderers.Count];
			for(var r = 0; r < renderers.Count; ++r) lodList[r].renderers = new[] { renderers[r].renderer };
			ConfigureLODGroup(lodGroup, lodList);

			// Configure the renderers and materials
			foreach(var lod in lodGroup.GetLODs()) {
				foreach(var renderer in lod.renderers) {
					SetLightmapScale(renderer);
					foreach(var material in renderer.sharedMaterials) {
						UseFadingShader(material);
					}
				}
			}
		}

		// TODO: These defaults should be extracted for use in other scripts 

		// Invariant vertical screen fraction at which object will be culled
		public static float culledLoD = 2f / 720f;

		// Fraction of lower detail blended blended in higher detail interval
		public static float fadeTransitionWidth = 0.25f;

		// OPTION: If LOD depends on bounding-box an adjustment could be made for objects
		// with large bounding boxes but small visible cross-sections (e.g. diagonal cylinders)
		// NOTE: LOD only considers the maximum axis of the bounding box (see editor visualization)
		// OPTION: A better approach to LoD fractions might be to compare vertex counts
		// OPTION: Even better would be to assess visual disparity due to faceting
		// OPTION: In the case of transparent objects, LoD modifications could:
		// - make the object opaque
		// - cull the object sooner & transition to full transparency + no reflection

		static float screenRelativeTransitionHeight(int lod, int lodCount) {
			if(lod + 1 >= lodCount) return culledLoD;
			return Mathf.Pow(0.5f, lod + 1);
		}

		public static void ConfigureLODGroup(LODGroup lodGroup, LOD[] lodList) {
			if(EP.useEditorUndo) Undo.RecordObject(lodGroup.gameObject, "Configure LODGroup");

			// Configure all transition fractions
			for(int f = 0; f < lodList.Length; ++f)
				lodList[f].screenRelativeTransitionHeight = screenRelativeTransitionHeight(f, lodList.Length);

			// Configure crossfade relative to transition fractions
			// NOTE: Final transition is to culled rendering
			lodGroup.fadeMode = LODFadeMode.CrossFade;
			lodGroup.animateCrossFading = false;
			for(int f = 0; f < lodList.Length; ++f) {
				var lastHeight = f - 1 >= 0 ? lodList[f - 1].screenRelativeTransitionHeight : 1f;
				var thisHeight = lodList[f].screenRelativeTransitionHeight;
				var nextHeight = f + 1 < lodList.Length ? lodList[f + 1].screenRelativeTransitionHeight : 0f;
				lodList[f].fadeTransitionWidth = fadeTransitionWidth * (thisHeight - nextHeight) / (lastHeight - thisHeight);
				// fadeTransitionWidth is entirely in this LoD, but is proportionate to the next LoD.
				// This is important for culling, which should not begin until within fadeTransitionWidth of the culling height
			}

			lodGroup.SetLODs(lodList);
			lodGroup.RecalculateBounds();
		}

		/// <summary>
		/// Sets the scale in the lightmap
		/// </summary>
		/// <remarks>
		/// LoD inclusion multiplies m_ScaleInLightmap by the screenRelativeTransitionHeight
		/// so a nominal scale of 1 will bake relative to the nominal LoD group scale.
		/// </remarks>
		public static void SetLightmapScale(Renderer renderer, float scale = 1f) {
			var so = new SerializedObject(renderer);
			so.FindProperty("m_ScaleInLightmap").floatValue = scale;
			so.ApplyModifiedProperties();
		}

		public const string fadingShaderName = "StandardCrossfade";
		static Shader _crossfadeShader = null;
		public static Shader crossfadeShader {
			get {
				if(_crossfadeShader == null) _crossfadeShader = Shader.Find(fadingShaderName);
				return _crossfadeShader;
			}
		}

		// QUESTION: Should instancing be enabled to support prefabs?
		// Dynamic objects, and lower levels of detail will be non-static.
		// https://docs.unity3d.com/Manual/GPUInstancing.html

		/// <summary>
		/// Replace Unity Standard shader with a shader supporting LOD blending
		/// </summary>
		/// <remarks>
		/// This modifies the shared material asset.
		/// </remarks>
		public static void UseFadingShader(Material material) {
			if(!crossfadeShader) return;
			if(material.shader.name != "Standard") return;
			if(PrefabUtility.IsPartOfImmutablePrefab(material)) {
				Debug.LogWarning("Material " + material.name + " is in ImmutablePrefab -> unable to substitute crossfade shader");
				return;
			}

			var renderType = material.GetTag("RenderType", false);
			material.shader = crossfadeShader;

			// PROBLEM: Initial material import does not initialize RenderType and RenderQueue
			// NOTE: This results in dithering of shadows behind transparent materials
			// For standard material configurations see:
			// https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/Inspector/StandardShaderGUI.cs
			// SetupMaterialWithBlendMode(Material material, BlendMode blendMode)
			switch(renderType) {
			case "Transparent":
				material.SetOverrideTag("RenderType", "Transparent");
				material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
				break;
			case "TransparentCutout":
				material.SetOverrideTag("RenderType", "TransparentCutout");
				material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
				break;
			default: // "Opaque"
				material.SetOverrideTag("RenderType", "");
				material.renderQueue = -1;
				break;
			}
		}
	}
}
