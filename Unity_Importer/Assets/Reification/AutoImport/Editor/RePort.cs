// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Reification {
	public class RePort : AssetPostprocessor {
		// This preprocessor pertains only to models in this path
		public const string importPath = "Assets/RePort/";

		/// <summary>
		/// Element type of exported model file
		/// </summary>
		/// <remarks>
		/// Element names match substrings in file names
		/// </remarks>
		public enum Element: int {
			/// <summary>
			/// Single file import (default)
			/// </summary>
			single = 0,

			/// <summary>
			/// Mesh surfaces and lights
			/// </summary>
			meshes = 1,

			/// <summary>
			/// Placeholder tetrahedra for prefab instances
			/// </summary>
			places = 2,

			/// <summary>
			/// Level of detail version for parametric surfaces
			/// </summary>
			/// <remarks>
			/// Multiple detail version may be present can be distinguished
			/// by integers following the element name (e.g. "detail0")
			/// </remarks>
			detail = 3
		}

		/// <summary>
		/// Parse exported file name of model or model element
		/// </summary>
		/// <remarks>
		/// File names are expected to be formatted as "[path]/[model].[element].[source].[filetype]"
		/// The suffix and source fields are optional, but will be included when by RePort compatible exporters.
		/// If source name is not recognized the element field will not be checked so the model will be created from this single element.
		/// If the element name is not recognized the model will be created from this single element.
		/// </remarks>
		/// <param name="assetPath">Path to file, including file name</param>
		/// <param name="path">The path to the model, will be the same for all elements and constituent models</param>
		/// <param name="name">The model base name, will be the same for all elements</param>
		/// <param name="element">Optionally empty: The model element identifier, used to configure import</param>
		/// <param name="exporter">Optionally empty: The model source application, used to reconcile coordinates</param>
		/// <param name="type">The model file type</param>
		static public void ParseModelName(string assetPath, out string path, out string name, out Element element, out string exporter, out string type) {
			path = "";
			name = "";
			element = Element.single;
			exporter = "";
			type = "";

			var pathParts = assetPath.Split('/');
			var pathIndex = pathParts.Length - 1;
			if(pathIndex < 0) return;
			path = pathParts[0];
			for(int p = 1; p < pathIndex - 1; ++p) path += '/' + pathParts[p];

			var nameParts = pathParts[pathIndex].Split('.');
			var nameIndex = nameParts.Length - 1;
			if(nameIndex < 0) return;
			if(nameIndex > 0) {
				type = nameParts[nameIndex];
				--nameIndex;
			}
			if(nameIndex > 0) {
				if(importerDict.ContainsKey(nameParts[nameIndex])) {
					exporter = nameParts[nameIndex];
					--nameIndex;
				}
			}
			if(nameIndex > 0 && exporter.Length > 0) {
				if(nameParts[nameIndex].StartsWith(Element.meshes.ToString())) element = Element.meshes;
				if(nameParts[nameIndex].StartsWith(Element.places.ToString())) element = Element.places;
				if(nameParts[nameIndex].StartsWith(Element.detail.ToString())) element = Element.detail;
				if(element != Element.single) --nameIndex;
			}
			name = nameParts[0];
			for(int p = 1; p <= nameIndex; ++p) name += '/' + nameParts[p];
		}

		/// <summary>
		/// Import post-processing delegates for model exporter application
		/// </summary>
		/// <remarks>
		/// The importer delgates modify the imported model prefab, which is otherwise immutable.
		/// These methods are intended to complement the application export process.
		/// </remarks>
		public interface Importer {
			/// <summary>
			/// Filename suffix identifying exporter application
			/// </summary>
			string exporter { get; }

			/// <summary>
			/// Transforms and meshes can be modified during this call
			/// </summary>
			/// <remarks>
			/// Called from OnPostprocessMeshHierarchy, which is called once
			/// for each direct child of the model root transform.
			/// </remarks>
			void ImportHierarchy(Transform hierarchy, Element element);

			/// <summary>
			/// Material properties can be modified during this call
			/// </summary>
			/// <remarks>
			/// Called from OnPostprocessMaterial so textures are not yet associated.
			/// </remarks>
			/// <param name="material">Model material being imported</param>
			void ImportMaterial(Material material, Element element);

			/// <summary>
			/// Model components can be modified during this call
			/// </summary>
			/// <remarks>
			/// Called from OnPostprocessModel so 
			/// Assets created during this call cannot be referenced.
			/// </remarks>
			/// <param name="model"></param>
			/// <param name="element"></param>
			void ImportModel(GameObject model, Element element);
		}

		static Dictionary<string, Importer> importerDict = new Dictionary<string, Importer>();

		/// <summary>
		/// Register import post-processing
		/// </summary>
		static public void RegisterImporter(Importer importer) {
			if(importerDict.ContainsKey(importer.exporter)) importerDict[importer.exporter] = importer;
			else importerDict.Add(importer.exporter, importer);
			// QUESTION: How can conflicts be identified when editing importers?
		}

		public RePort() {
			// Ensure that import path exists
			EP.CreatePersistentPath(importPath.Substring("Assets/".Length));
		}

		void OnPreprocessModel() {
			if(!assetPath.StartsWith(importPath)) return;
			if(importAssets.Contains(assetPath)) return;
			Debug.Log($"RePort.OnPreprocessModel({assetPath})");

			var modelImporter = assetImporter as ModelImporter;
			if(modelImporter.importSettingsMissing) {
				// Configure import
				ParseModelName(assetPath, out _, out _, out var element, out _, out _);
				switch(element) {
				case Element.places:
					PlacesImporter(modelImporter);
					break;
				default:
					MeshesImporter(modelImporter);
					break;
				}
			} else {
				// Configure reimport
				ClearRemappedAssets(modelImporter);
				// FIXME: Selection revelas model in inspect, so if any external object mapping is removed
				// it will trigger a pop-up asking whether to apply or revert changes to import settings
			}
		}

		/// <summary>
		/// Clear all externally remapped assets
		/// </summary>
		/// <remarks>
		/// After extracting textures and materials the model importer may maintain a remapping.
		/// The model will attempt to reference these assets during reimport, even if they
		/// have been deleted.
		/// ClearRemappedAssets will remove all map entries for assets that do not exist.
		/// </remarks>
		/// <param name="force">Remove remap even if external assets exist</param>
		static public void ClearRemappedAssets(ModelImporter modelImporter, bool force = false) {
			var externalObjectMap = modelImporter.GetExternalObjectMap();
			foreach(var map in externalObjectMap) {
				var identifier = map.Key;
				var exists = false;
				// PROBLEM: path & guid will be defined even if asset no longer exists.
				// SOLUTION: Use filesystem to check asset exists.
				var path = AssetDatabase.GetAssetPath(map.Value);
				if(!force && path != null && path.StartsWith("Assets/")) {
					path = Application.dataPath + "/" + path.Substring("Assets/".Length);
					exists = File.Exists(path);
					if(!exists) Debug.Log($"File path {path} in AssetDatabase does not exist");
				}
				if(!exists) modelImporter.RemoveRemap(identifier);
			}
		}

		/// <summary>
		/// Import configuration to enable lightmapping
		/// </summary>
		static public void MeshesImporter(ModelImporter modelImporter) {
			modelImporter.generateSecondaryUV = true;
			// CRITICAL: Generation after meshes are extracted is not possible
			// WARNING: Lightmap UV generation is very slow
			// https://docs.unity3d.com/Manual/LightingGiUvs-GeneratingLightmappingUVs.html

			modelImporter.importLights = true; // Requires conversion from physical units
			modelImporter.importCameras = false;
			modelImporter.importBlendShapes = false;
			modelImporter.importVisibility = false;
			modelImporter.preserveHierarchy = false;
			modelImporter.sortHierarchyByName = false;
			modelImporter.addCollider = false; // Will be added after LOD combination
			modelImporter.useFileScale = true;

			modelImporter.isReadable = true;
			modelImporter.meshCompression = ModelImporterMeshCompression.Off;
			modelImporter.meshOptimizationFlags = MeshOptimizationFlags.Everything;

			modelImporter.keepQuads = false;
			modelImporter.weldVertices = true;
			modelImporter.importNormals = ModelImporterNormals.Import;
			modelImporter.importTangents = ModelImporterTangents.CalculateMikk;
		}

		/// <summary>
		/// Import to preserve transform information in placeholder meshes
		/// </summary>
		static public void PlacesImporter(ModelImporter modelImporter) {
			modelImporter.generateSecondaryUV = false;

			modelImporter.importLights = false;
			modelImporter.importCameras = false;
			modelImporter.importBlendShapes = false;
			modelImporter.importVisibility = false;
			modelImporter.preserveHierarchy = false;
			modelImporter.sortHierarchyByName = false;
			modelImporter.addCollider = false;
			modelImporter.useFileScale = true;

			modelImporter.isReadable = true;
			modelImporter.meshCompression = ModelImporterMeshCompression.Off;
			modelImporter.meshOptimizationFlags = 0;
			// IMPORTANT: Mesh optimization must be disabled in order to preserve
			// instance data encoded in mesh vertices.

			modelImporter.keepQuads = true;
			modelImporter.weldVertices = false;
			modelImporter.importNormals = ModelImporterNormals.None;
			modelImporter.importTangents = ModelImporterTangents.None;
		}

		void OnPostprocessMeshHierarchy(GameObject child) {
			if(!assetPath.StartsWith(importPath)) return;
			if(importAssets.Contains(assetPath)) return;
			//Debug.Log($"RePort.OnPostprocessMeshHierarchy({assetPath}/{child.name})");

			ParseModelName(assetPath, out _, out _, out var element, out var source, out _);
			if(importerDict.ContainsKey(source)) importerDict[source].ImportHierarchy(child.transform, element);
		}

		void OnPostprocessMaterial(Material material) {
			if(!assetPath.StartsWith(importPath)) return;
			if(importAssets.Contains(assetPath)) return;
			//Debug.Log($"RePort.OnPostprocessMaterial({assetPath}/{material.name})");

			ParseModelName(assetPath, out _, out _, out var element, out var source, out _);
			if(importerDict.ContainsKey(source)) importerDict[source].ImportMaterial(material, element);
		}

		void OnPostprocessModel(GameObject model) {
			if(!assetPath.StartsWith(importPath)) return;
			if(importAssets.Contains(assetPath)) return;
			Debug.Log($"RePort.OnPostprocessModel({assetPath})");

			// Strip empty GameObjects from hierarchy
			RemoveEmpty(model);

			ParseModelName(assetPath, out _, out _, out var element, out var source, out _);
			if(importerDict.ContainsKey(source)) importerDict[source].ImportModel(model, element);

			// Enqueue model for processing during editor update
			// PROBLEM: During import (including during OnPostprocessAllAssets)
			// AssetDatabase is implicitly subject to StartAssetEditing()
			// This prevents created asset discovery, and also prevents created collider physics
			// SOLUTION: Enqueue asset combine, assemble & configure steps after import concludes
			importAssets.Add(assetPath);
			EditorApplication.update += ProcessImportedModels;
		}

		/// <summary>
		/// Removes all empty branches in the hierarchy of a GameObject
		/// </summary>
		/// <remarks>
		/// Excluding cameras or lights from a model import removes components,
		/// but their associated GameObjects will persist in the hierarchy.
		/// </remarks>
		static public void RemoveEmpty(GameObject gameObject) {
			// IMPORTANT: Before counting children, apply RemoveEmpty to children
			// since their removal could result in children being empty
			var children = gameObject.transform.Children();
			foreach(var child in children) RemoveEmpty(child.gameObject);

			// IMPORTANT: Before counting components, remove empty meshes
			// since this could result in components being empty
			var meshFilter = gameObject.GetComponent<MeshFilter>();
			if(meshFilter) {
				var sharedMesh = meshFilter.sharedMesh;
				if(sharedMesh == null || sharedMesh.vertexCount == 0) {
					var meshRenderer = gameObject.GetComponent<MeshRenderer>();
					if(meshRenderer != null) EP.Destroy(meshRenderer);
					var meshCollider = gameObject.GetComponent<MeshCollider>();
					if(meshCollider != null) EP.Destroy(meshCollider);
					EP.Destroy(meshFilter);
				}
			}

			// Remove if no children and no components
			if(
				gameObject.transform.childCount == 0 &&
				gameObject.GetComponents<Component>().Length == 1
			) {
				EP.Destroy(gameObject);
				return;
			}
		}

		static HashSet<string> importAssets = new HashSet<string>();

		static void ProcessImportedModels() {
			// Ensure that ProcessImportedModels is called only once per import batch
			// IMPORTANT: Unregistering must occur before any possible import exception
			// Otherwise the editor will deadlock while repeatedly attempting to import.
			EditorApplication.update -= ProcessImportedModels;

			// PROBLEM: Unsubscribing from EditorApplication.update is not immediate - multiple callbacks may be received
			// SOLUTION: Abort immediately if importAssets is empty
			if(importAssets.Count == 0) return;

			// TODO: Progress bar popup

			// There are 3 import types to consider:
			// Partial models, which are in a subfolder of importPath and have a suffix
			// Complete models, which are in a subfolder of importPath and have no suffix
			// Assembled models, which are in the importPath folder
			var partialModels = new Dictionary<string, List<GameObject>>();
			var completeModels = new List<GameObject>();
			var assembledModels = new List<GameObject>();

			foreach(var modelPath in importAssets) {
				Debug.Log($"RePort.ProcessImportedModels(): modelPath = {modelPath}");

				// TODO: Skip this step for places model element
				// Extract all assets from each imported model
				var model = ExtractAssets(modelPath);

				// Classify models according to path and suffix
				var mergePath = modelPath.Substring(0, modelPath.LastIndexOf('/'));
				if(mergePath == importPath.Substring(0, importPath.Length - 1)) {
					completeModels.Add(model);
				} else {
					ParseModelName(modelPath, out _, out _, out var element, out _, out _);
					if(element != Element.single) {
						if(!partialModels.ContainsKey(mergePath)) partialModels.Add(mergePath, new List<GameObject>());
						partialModels[mergePath].Add(model);
					} else {
						completeModels.Add(model);
					}
				}
			}
			importAssets.Clear();

			CombinePartial(partialModels, completeModels);
			AssembleComplete(completeModels, assembledModels);
			var configured = ConfigureAssembled(assembledModels);

			// If only one model was imported, open it
			if(configured.Count == 1) EditorSceneManager.OpenScene(configured[0], OpenSceneMode.Single);
		}

		/// <summary>
		/// Creates an indepedent prefab
		/// </summary>
		/// <remarks>
		/// All assets used by model are copied into an adjacent folder.
		/// ExtractAssets calls ExtractTextures - there is no need to call it first.
		/// </remarks>
		static public GameObject ExtractAssets(string modelPath) {
			// IMPORTANT: Textures must be extracted and remapped before materials are extracted
			ExtractTextures(modelPath);

			// Create model prefab and extract material copies
			var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
			var modelPathRoot = modelPath.Substring(0, modelPath.LastIndexOf('.'));
			GatherAssets.ApplyTo(model, modelPathRoot);

			// Load prefab created by GatherAssets
			return AssetDatabase.LoadAssetAtPath<GameObject>(modelPathRoot + ".prefab");
		}

		/// <summary>
		/// Extracts and remaps textures for use by materials
		/// </summary>
		/// <remarks>
		/// IMPORTANT: In order for extracted textures to be remapped to materials
		/// this must be called when AssetDatabase.StartAssetEditing() does not pertain
		/// so that textures can be synchronously imported for remapping.
		/// 
		/// WARNING: In order to update model materials the model will be remiported,
		/// so if import triggers this call recursion must be prevented.
		/// 
		/// For the implementation of the "Extract Textures" button 
		/// in the "Materials" tab of the "Import Settings" Inspector panel, see:
		/// https://github.com/Unity-Technologies/UnityCsReference/
		/// Modules/AssetPipelineEditor/ImportSettings/ModelImporterMaterialEditor.cs
		/// private void ExtractTexturesGUI()
		/// </remarks>
		static public void ExtractTextures(string modelPath) {
			var modelImporter = AssetImporter.GetAtPath(modelPath) as ModelImporter;
			if(modelImporter == null) return;

			// Extract textures
			var texturesPath = modelPath.Substring(0, modelPath.LastIndexOf('.')) + "/Textures";
			try {
				AssetDatabase.StartAssetEditing();
				modelImporter.ExtractTextures(texturesPath);
			} finally {
				AssetDatabase.StopAssetEditing();
				AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
				// Import textures to AssetDatabase
			}

			// If no textures were imported remove folder & skip the reimport
			// NOTE: ExtractTextures will only create the texturesPath if there are textures to be extracted
			if(EP.CreatePersistentPath(texturesPath.Substring("Assets/".Length), false) > 0) return;

			// Remap textures and reimport model
			// NOTE: Remapping will fail while StartAssetEditing() pertains (during model import)
			// since extracted textures will not be immediately imported, and so will not be found.
			try {
				AssetDatabase.StartAssetEditing();
				var guids = AssetDatabase.FindAssets("t:Texture", new string[] { texturesPath });
				foreach(var guid in guids) {
					var path = AssetDatabase.GUIDToAssetPath(guid);
					var texture = AssetDatabase.LoadAssetAtPath<Texture>(path);
					if(texture == null) continue;
					var identifier = new AssetImporter.SourceAssetIdentifier(texture);
					modelImporter.AddRemap(identifier, texture);
				}
			} finally {
				AssetDatabase.StopAssetEditing();
				AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);
				// Update model materials with texture remapping.
			}

			// TODO: Avoid the pop-up requesting to fix normalmap texture types (ideally by identifying as normalmap)
		}

		// Combines partial models (meshes and levels of detail and prefab places) into complete models
		static void CombinePartial(Dictionary<string, List<GameObject>> partialModels, List<GameObject> completeModels) {
			// Find or make a merged prefab for each folder in the path
			// IMPORTANT: This must be done BEFORE ReplacePrefabs.ApplyTo()
			// so that prefabs can be linked immediately and updated subsequently.
			// NOTE: Existing merged prefabs will be updated by new imports
			var mergedPrefabs = new Dictionary<string, GameObject>();
			foreach(var path in partialModels.Keys) CreateMerged(path, mergedPrefabs);
			AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

			try {
				AssetDatabase.StartAssetEditing();
				// Merge prefab consistuents and copy assets
				// NOTE: If new constituents are added or updated they will be merged into existing model.
				// NOTE: If additional replicated models are added they will replace locators
				// in the merged model, even if the corresponding instances constituent was previously merged.
				foreach(var item in mergedPrefabs) {
					if(partialModels.ContainsKey(item.Key)) MergeModels.ApplyTo(item.Value, partialModels[item.Key].ToArray());
					// Assets will be gathered in folder adjacent to merged model
					// IMPORTANT: Prefabs will maintain independent asset copies.
					var gatherer = new GatherAssets.AssetGatherer(item.Key);
					gatherer.CopyAssets(item.Value);
					completeModels.Add(item.Value);
					//Debug.Log($"Gathered assets from {item.Value.name} to {item.Key}");
				}
			} finally {
				AssetDatabase.StopAssetEditing();
				AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
			}
		}

		// Creates a merged prefab assets at each folder inside of import path
		static void CreateMerged(string path, Dictionary<string, GameObject> mergedPrefabs) {
			var pathPart = path;
			// TODO: This only needs to recurse once
			while(pathPart.StartsWith(importPath)) {
				if(!mergedPrefabs.ContainsKey(pathPart)) {
					// Find or make a merged prefab
					var mergedPath = pathPart + ".prefab";
					var merged = AssetDatabase.LoadAssetAtPath<GameObject>(mergedPath);
					if(!merged) {
						var empty = EP.Instantiate();
						empty.name = pathPart.Substring(pathPart.LastIndexOf('/') + 1);
						// WARNING: SaveAsPrefabAsset will return null while AssetDatabase.StartAssetEditing() pertains
						merged = PrefabUtility.SaveAsPrefabAsset(empty, mergedPath);
						EP.Destroy(empty);
						//Debug.Log($"Created empty merged object: {pathPart}.prefab");
					}
					mergedPrefabs.Add(pathPart, merged);
				}
				pathPart = pathPart.Substring(0, pathPart.LastIndexOf('/'));
				// IMPORTANT: this will not create an importPath named prefab, 
				// since importPath ends with '/' which Substring defining pathPart will remove
			}
		}

		// Swap assets, replace prefabs, gather levels of detail, add colliders
		static void AssembleComplete(List<GameObject> completeModels, List<GameObject> assembledModels) {
			try {
				AssetDatabase.StartAssetEditing();
				foreach(var model in completeModels) {
					var prefabPath = AssetDatabase.GetAssetPath(model);
					var searchPath = prefabPath.Substring(0, prefabPath.Length - ".prefab".Length);

					// Assembled models will contain only prefabs in their associated folder
					// Constituent models may contain other constituent models, so search should begin adjacent
					var isAssembledModel = !searchPath.Substring(importPath.Length).Contains("/");
					if(!isAssembledModel) searchPath = searchPath.Substring(0, searchPath.LastIndexOf('/'));

					// IMPORTANT: Prefab replacement must happen after merged assets are imported, but before assets are swapped
					// IMPORTANT: Since prefabs are not copied after replacement, this ensures that the prefabs can be updated
					ReplacePrefabs.ApplyTo(model, searchPath);

					// Swap assets for copies created when combining partial models
					// NOTE: Modifications will not alter original assets, since copies are now being used
					var gatherer = new GatherAssets.AssetGatherer(searchPath);
					gatherer.SwapAssets(model);

					// Configure the complete model
					AutoLOD.ApplyTo(model);
					AutoStatic.ApplyTo(model);
					AutoColliders.ApplyTo(model);
					//Debug.Log($"Configured prefab: {prefabPath}");

					// Only create scenes from prefabs in RePort
					if(isAssembledModel) assembledModels.Add(model);
				}
			} finally {
				AssetDatabase.StopAssetEditing();
				AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
			}
		}

		// Create scene for assembled model and generate lighting
		static List<string> ConfigureAssembled(List<GameObject> assembledModels) {
			var configuredScenes = new List<string>();
			foreach(var model in assembledModels) {
				var modelPath = AssetDatabase.GetAssetPath(model);
				modelPath = modelPath.Substring(0, modelPath.Length - ".prefab".Length);
				var scenePath = AutoScene.ApplyTo(modelPath, model);
				// TODO: Hook to add configurable prefabs...
				AutoLightmaps.ApplyTo(scenePath);
				// TODO: Hook to bake Reflections, Acoustics...
				//Debug.Log($"Created scene : {scenePath}");
				configuredScenes.Add(scenePath);
			}
			return configuredScenes;
		}
	}
}
