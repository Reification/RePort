// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

// TODO: DO NOT MODIFY when references are already gathered
// https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/statements-expressions-operators/how-to-test-for-reference-equality-identity
// NOTE: More simply: compare asset GUID

// TODO: Replacement rules: materials should persist from existing assets,
// Meshes should update on new assets...

// TODO: It should be possible to specify whether prefabs are copied (and likewise for other assets)

// DECISION: Avoid name collisions: assume each prefab is independent in terms of materials, meshes, textures...
// GOAL: Constituent prefab arrangement and subdirectories should match model importing.

namespace Reification {
	/// <summary>
	/// Gather assets associated with a model by creating and using copies
	/// </summary>
	/// <remarks>
	/// Assets will be gathered in a folder created adjacent to the target,
	/// with the same name as the target.
	/// If matching assets are already present in the folder, 
	/// those will be used instead of the targets.
	/// This ensures that repeatedly imported materials or meshes will
	/// yield only one referenced instance.
	/// This also ensures that re-importing after a configuration will preserve
	/// modifications, such as material adjustments.
	/// </remarks>
	public class GatherAssets {
		const string menuItemName = "Reification/Gather Assets";
		const int menuItemPriority = 22;

		[MenuItem(menuItemName, validate = true, priority = menuItemPriority)]
		static private bool Validate() {
			if(Selection.gameObjects.Length == 0) return false;
			return true;
		}

		[MenuItem(menuItemName, priority = menuItemPriority)]
		static private void Execute() {
			Undo.IncrementCurrentGroup();
			Undo.SetCurrentGroupName("Gather Assets");

			foreach(var gameObject in Selection.gameObjects) {
				var pathRoot = AssetDatabase.GetAssetOrScenePath(gameObject);
				pathRoot = pathRoot.Substring(0, pathRoot.LastIndexOf('/')) + "/" + gameObject.name;
				ApplyTo(gameObject, pathRoot);
			}
		}

		static public void ApplyTo(GameObject gameObject, string pathRoot) {
			var assetType = PrefabUtility.GetPrefabAssetType(gameObject);
			if(assetType == PrefabAssetType.MissingAsset) return;
			if(assetType == PrefabAssetType.Model) {
				// In the case of a model created an editable prefab
				var prefab = EP.Instantiate(gameObject);
				PrefabUtility.UnpackPrefabInstance(prefab, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
				gameObject = PrefabUtility.SaveAsPrefabAsset(prefab, pathRoot + ".prefab");
				EP.Destroy(prefab);
			}

			// NOTE: Calling CopyAssets will be extremely slow if each asset is imported individually.
			// Instead, all of the copying will be done in one batch, after which assets will be imported.
			// Then, a new instance of AssetGatherer will find those copies and make replacements in
			// a second batch.
			try {
				AssetDatabase.StartAssetEditing();
				var assertGatherer = new AssetGatherer(pathRoot);
				assertGatherer.CopyAssets(gameObject);
			} finally {
				AssetDatabase.StopAssetEditing();
				AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
			}
			try {
				AssetDatabase.StartAssetEditing();
				var assertGatherer = new AssetGatherer(pathRoot);
				assertGatherer.SwapAssets(gameObject);
			} finally {
				AssetDatabase.StopAssetEditing();
				AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
			}
		}

		// TODO: This should go to material extensions
		// NOTE: This does not check shader type - other materials may use some or all of the same names
		static public Dictionary<string, Texture> GetStandardMaterialTextures(Material material) {
			var textures = new Dictionary<string, Texture>() {
			{ "_MainTex", null }, // Albedo
			{ "_MetallicGlossMap", null }, // Metallicity
			{ "_BumpMap", null }, // NormalMap
			{ "_ParallaxMap", null }, // HeightMap
			{ "_OcclusionMap", null }, // Occlusion
			{ "_EmissionMap", null }, // Emission
			{ "_DetailMask", null }, // DetailMask
			{ "_DetailAlbedoMap", null }, // DetailAlbedo
			{ "_DetailNormalMap", null }, // DetailNormal
		};
			var keys = new List<string>(textures.Keys);
			foreach(var key in keys) {
				var texture = material.GetTexture(key);
				if(texture) textures[key] = material.GetTexture(key);
				else textures.Remove(key);
			}
			return textures;
		}

		public class TextureGatherer {
			const string textureFolder = "Textures";
			string texturePath;
			public Dictionary<string, Texture> textureAssets = new Dictionary<string, Texture>();

			public TextureGatherer(string pathRoot) {
				texturePath = pathRoot + "/" + textureFolder;
				if(EP.CreatePersistentPath(texturePath.Substring("Assets/".Length), false) > 0) return;
				var textureGUIDs = AssetDatabase.FindAssets("t:Texture", new[] { texturePath });
				foreach(var guid in textureGUIDs) {
					var asset = AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(guid));
					textureAssets.Add(asset.name, asset);
				}
			}

			public void CopyTextures(Material material) {
				if(material.shader.name != "Standard") return;
				var shaderTextures = GetStandardMaterialTextures(material);
				foreach(var shaderTexture in shaderTextures) {
					var texture = shaderTexture.Value;
					if(textureAssets.ContainsKey(texture.name)) continue;
					// IMPORTANT: Default textures may be in use and are not in Assets path
					if(!AssetDatabase.GetAssetPath(texture).StartsWith("Assets/")) continue;
					var copyTexture = EP.CopyAssetToPath(texture, texturePath.Substring("Assets/".Length));
					textureAssets.Add(texture.name, copyTexture);
				}
			}

			public void SwapTextures(Material material) {
				if(material.shader.name != "Standard") return;
				var shaderTextures = GetStandardMaterialTextures(material);
				foreach(var shaderTexture in shaderTextures) {
					var texture = shaderTexture.Value;
					if(!textureAssets.ContainsKey(texture.name)) continue;
					texture = textureAssets[texture.name];
					if(!material) {
						Debug.LogWarning($"Created texture {texture.name} could not be loaded -> stop editing assets, then reconstruct TextureGatherer");
						continue;
					}
					material.SetTexture(shaderTexture.Key, texture);
				}
			}
		}

		public class MaterialGatherer {
			const string materialFolder = "Materials";
			string materialPath;
			public Dictionary<string, Material> materialAssets = new Dictionary<string, Material>();

			public MaterialGatherer(string pathRoot) {
				materialPath = pathRoot + "/" + materialFolder;
				if(EP.CreatePersistentPath(materialPath.Substring("Assets/".Length), false) > 0) return;
				var materialGUIDs = AssetDatabase.FindAssets("t:Material", new[] { materialPath });
				foreach(var guid in materialGUIDs) {
					var asset = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
					materialAssets.Add(asset.name, asset);
				}
			}

			public void CopyMaterials(Renderer renderer) {
				var sharedMaterials = renderer.sharedMaterials;
				for(var m = 0; m < sharedMaterials.Length; ++m) {
					var material = sharedMaterials[m];
					if(!material) continue;
					if(materialAssets.ContainsKey(material.name)) continue;
					// IMPORTANT: Default materials may be in use and are not in Assets path
					if(!AssetDatabase.GetAssetPath(material).StartsWith("Assets/")) continue;
					var copyMaterial = EP.CopyAssetToPath(material, materialPath.Substring("Assets/".Length), ".mat");
					materialAssets.Add(material.name, copyMaterial);
				}
			}

			public void SwapMaterials(Renderer renderer) {
				var sharedMaterials = renderer.sharedMaterials;
				for(var m = 0; m < sharedMaterials.Length; ++m) {
					var material = sharedMaterials[m];
					if(!material) continue;
					if(!materialAssets.ContainsKey(material.name)) continue;
					material = materialAssets[material.name];
					if(!material) {
						Debug.LogWarning($"Created material {material.name} could not be loaded -> stop editing assets, then reconstruct MaterialGatherer");
						continue;
					}
					sharedMaterials[m] = material;
				}
				renderer.sharedMaterials = sharedMaterials;
			}
		}

		public class MeshGatherer {
			const string meshFolder = "Meshes";
			string meshPath;
			public Dictionary<string, Dictionary<int, Mesh>> meshAssets = new Dictionary<string, Dictionary<int, Mesh>>();

			// Detail versions of the same object are distinguished by vertex count
			string meshNameSuffix(Mesh mesh) {
				return $".{mesh.vertexCount}";
			}

			void expandMeshName(Mesh mesh) {
				var suffix = meshNameSuffix(mesh);
				if(!mesh.name.EndsWith(suffix)) mesh.name += suffix;
			}

			void reduceMeshName(Mesh mesh) {
				var suffix = meshNameSuffix(mesh);
				if(mesh.name.EndsWith(suffix)) mesh.name = mesh.name.Substring(0, mesh.name.Length - suffix.Length);
			}

			public MeshGatherer(string pathRoot) {
				meshPath = pathRoot + "/" + meshFolder;
				if(EP.CreatePersistentPath(meshPath.Substring("Assets/".Length), false) > 0) return;
				var meshGUIDs = AssetDatabase.FindAssets("t:Mesh", new[] { meshPath });
				foreach(var guid in meshGUIDs) {
					var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(guid));
					reduceMeshName(mesh);
					if(!meshAssets.ContainsKey(mesh.name)) meshAssets.Add(mesh.name, new Dictionary<int, Mesh>());
					meshAssets[mesh.name][mesh.vertexCount] = mesh;
				}
			}

			public void CopyMeshes(MeshFilter meshFilter) {
				var mesh = meshFilter.sharedMesh;
				reduceMeshName(mesh);
				if(
					meshAssets.ContainsKey(mesh.name) &&
					meshAssets[mesh.name].ContainsKey(mesh.vertexCount)
				) return;
				// IMPORTANT: Default meshes may be in use and are not in Assets path
				if(!AssetDatabase.GetAssetPath(mesh).StartsWith("Assets/")) return;
				expandMeshName(mesh);
				var copyMesh = EP.CopyAssetToPath(mesh, meshPath.Substring("Assets/".Length), ".asset");
				reduceMeshName(mesh);
				if(!meshAssets.ContainsKey(mesh.name)) meshAssets.Add(mesh.name, new Dictionary<int, Mesh>());
				meshAssets[mesh.name].Add(mesh.vertexCount, copyMesh);
			}

			public void SwapMeshes(MeshFilter meshFilter) {
				var mesh = meshFilter.sharedMesh;
				reduceMeshName(mesh);
				if(
					!meshAssets.ContainsKey(mesh.name) ||
					!meshAssets[mesh.name].ContainsKey(mesh.vertexCount)
				) return;
				mesh = meshAssets[mesh.name][mesh.vertexCount];
				if(!mesh) {
					Debug.LogWarning($"Created mesh {mesh.name} could not be loaded -> stop editing assets, then reconstruct MeshGatherer");
					return;
				}
				meshFilter.sharedMesh = mesh;
			}
		}

		public class PrefabGatherer {
			const string prefabFolder = "Prefabs";
			string prefabPath;
			public Dictionary<string, GameObject> prefabAssets = new Dictionary<string, GameObject>();

			public PrefabGatherer(string pathRoot) {
				prefabPath = pathRoot + "/" + prefabFolder;
				if(EP.CreatePersistentPath(prefabPath.Substring("Assets/".Length), false) > 0) return;
				var prefabGUIDs = AssetDatabase.FindAssets("t:GameObject", new[] { prefabPath });
				foreach(var guid in prefabGUIDs) {
					var asset = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
					prefabAssets.Add(asset.name, asset);
				}
			}

			// Creates copies of all child prefabs
			public void CopyPrefabs(GameObject gameObject) {
				var children = gameObject.transform.Children();
				foreach(var child in children) {
					if(PrefabUtility.GetPrefabAssetType(child.gameObject) == PrefabAssetType.NotAPrefab) continue;
					var prefab = PrefabUtility.GetCorrespondingObjectFromSource<GameObject>(child.gameObject);
					if(prefabAssets.ContainsKey(prefab.name)) continue;
					var copyPrefab = EP.CopyAssetToPath(prefab, prefabPath.Substring("Assets/".Length));
					prefabAssets.Add(prefab.name, copyPrefab);
				}
			}

			// Replaces all child prefabs with existing copies
			public void SwapPrefabs(GameObject gameObject) {
				var children = gameObject.transform.Children();
				foreach(var child in children) {
					if(PrefabUtility.GetPrefabAssetType(child.gameObject) == PrefabAssetType.NotAPrefab) continue;
					var prefab = PrefabUtility.GetCorrespondingObjectFromSource<GameObject>(child.gameObject);
					if(!prefabAssets.ContainsKey(prefab.name)) continue;
					prefab = prefabAssets[prefab.name];
					if(!prefab) {
						Debug.LogWarning($"Created prefab {prefab.name} could not be loaded -> stop editing assets, then reconstruct PrefabGatherer");
						continue;
					}
					// Replace the child with instance of gathered prefab
					var instance = EP.Instantiate(prefab).transform;
					instance.name = child.name;
					EP.SetParent(instance, child.parent);
					instance.localPosition = child.localPosition;
					instance.localRotation = child.localRotation;
					instance.localScale = child.localScale;
					// QUESTION: Is there a general way to transfer overrides?
					// https://docs.unity3d.com/ScriptReference/PrefabUtility.GetObjectOverrides.html
					EP.Destroy(child.gameObject);
				}
			}
		}

		public class AssetGatherer {
			PrefabGatherer prefabGatherer;
			MaterialGatherer materialGatherer;
			TextureGatherer textureGatherer;
			MeshGatherer meshGatherer;

			public AssetGatherer(
				string pathRoot
			) {
				prefabGatherer = new PrefabGatherer(pathRoot);
				materialGatherer = new MaterialGatherer(pathRoot);
				textureGatherer = new TextureGatherer(pathRoot);
				meshGatherer = new MeshGatherer(pathRoot);
			}

			HashSet<string> copyPrefabs = new HashSet<string>();
			HashSet<string> copyMaterials = new HashSet<string>();

			public void CopyAssets(GameObject gameObject) {
				using(var editScope = new EP.EditGameObject(gameObject)) {
					var editObject = editScope.editObject;

					foreach(var child in editObject.Children())
						if(PrefabUtility.GetPrefabAssetType(child) == PrefabAssetType.NotAPrefab) CopyAssets(child);

					var renderer = editObject.GetComponent<MeshRenderer>();
					if(renderer) {
						materialGatherer.CopyMaterials(renderer);
						foreach(var material in renderer.sharedMaterials) {
							if(copyMaterials.Contains(material.name)) continue;
							copyMaterials.Add(material.name);
							textureGatherer.CopyTextures(material);
						}
					}

					var meshFilter = editObject.GetComponent<MeshFilter>();
					if(meshFilter) meshGatherer.CopyMeshes(meshFilter);
					/*
					prefabGatherer.CopyPrefabs(editObject);
					foreach(var prefab in prefabGatherer.prefabAssets.Values) {
						if(copyPrefabs.Contains(prefab.name)) continue;
						copyPrefabs.Add(prefab.name);
						CopyAssets(prefab);
					}*/
				}
			}

			HashSet<string> swapPrefabs = new HashSet<string>();
			HashSet<string> swapMaterials = new HashSet<string>();

			public void SwapAssets(GameObject gameObject) {
				using(var editScope = new EP.EditGameObject(gameObject)) {
					var editObject = editScope.editObject;

					foreach(var child in editObject.Children())
						if(PrefabUtility.GetPrefabAssetType(child) == PrefabAssetType.NotAPrefab) SwapAssets(child);

					var renderer = editObject.GetComponent<MeshRenderer>();
					if(renderer) {
						materialGatherer.SwapMaterials(renderer);
						foreach(var material in renderer.sharedMaterials) {
							if(swapMaterials.Contains(material.name)) continue;
							swapMaterials.Add(material.name);
							textureGatherer.SwapTextures(material);
						}
					}

					var meshFilter = editObject.GetComponent<MeshFilter>();
					if(meshFilter) meshGatherer.SwapMeshes(meshFilter);
					/*
					prefabGatherer.SwapPrefabs(editObject);
					foreach(var prefab in prefabGatherer.prefabAssets.Values) {
						if(swapPrefabs.Contains(prefab.name)) continue;
						swapPrefabs.Add(prefab.name);
						SwapAssets(prefab);
					}*/
				}
			}
		}
	}
}
