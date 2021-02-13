// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Reification {
	#region static

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
		/// <param name="source">Optionally empty: The model source application, used to reconcile coordinates</param>
		/// <param name="type">The model file type</param>
		static public void ParseModelName(string assetPath, out string path, out string name, out Element element, out string source, out string type) {
			path = "";
			name = "";
			element = Element.single;
			source = "";
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
					source = nameParts[nameIndex];
					--nameIndex;
				}
			}
			if(nameIndex > 0 && source.Length > 0) {
				if(nameParts[nameIndex].StartsWith(Element.meshes.ToString())) element = Element.meshes;
				if(nameParts[nameIndex].StartsWith(Element.places.ToString())) element = Element.places;
				if(nameParts[nameIndex].StartsWith(Element.detail.ToString())) element = Element.detail;
				if(element != Element.single) --nameIndex;
			}
			name = nameParts[0];
			for(int p = 1; p <= nameIndex; ++p) name += '/' + nameParts[p];
		}

		/// <summary>
		/// Import post-processing depending on the export application
		/// </summary>
		public interface Importer {
			/// <summary>
			/// Filename suffix identifying exporting application
			/// </summary>
			string suffix { get; }

			/// <summary>
			/// Delegate called from OnPostprocessMeshHierarchy
			/// </summary>
			/// <remarks>
			/// OnPostprocessMeshHierarchy is called once for each child transform
			/// of the imported model root transform.
			/// </remarks>
			/// <param name="child">Model child transform being imported</param>
			/// <param name="element">Model element being importer</param>
			void ImportMeshHierarchy(Transform child, Element element);

			// TODO: Other processing hooks
		}

		static Dictionary<string, Importer> importerDict = new Dictionary<string, Importer>();

		/// <summary>
		/// Register import post-processing
		/// </summary>
		static public void RegisterImporter(Importer importer) {
			if(importerDict.ContainsKey(importer.suffix)) importerDict[importer.suffix] = importer;
			else importerDict.Add(importer.suffix, importer);
			// QUESTION: How can conflicts be identified when editing importers?
		}

		#endregion

		public RePort() {
			EP.CreatePersistentPath(importPath.Substring("Assets/".Length));
		}

		void OnPreprocessModel() {
			if(!assetPath.StartsWith(importPath)) return;
			if(importAssets.Contains(assetPath)) return;
			Debug.Log($"RePort.OnPreprocessModel()\nassetPath = {assetPath}");

			// Configure import
			var modelImporter = assetImporter as ModelImporter;
			ParseModelName(assetPath, out _, out _, out var element, out _, out _);
			switch(element) {
			case Element.places:
				PlacesImporter(modelImporter);
				break;
			default:
				MeshesImporter(modelImporter);
				break;
			}
			ClearRemappedAssets(modelImporter);
		}

		/// <summary>
		/// Clear all externally remapped assets
		/// </summary>
		/// <remarks>
		/// After gathering materials the model model importer will maintain a remapping.
		/// This will clear external remappings of every asset in the model, enabling
		/// reimporting to begin as if the model is new to the project.
		/// </remarks>
		static public void ClearRemappedAssets(ModelImporter modelImporter) {
			var externalObjectMap = modelImporter.GetExternalObjectMap();
			foreach(var identifier in externalObjectMap.Keys) modelImporter.RemoveRemap(identifier);
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
			// CRITICAL: Mesh optimization must be disabled in order to preserve
			// instance data encoded in mesh vertices.

			modelImporter.keepQuads = true;
			modelImporter.weldVertices = false;
			modelImporter.importNormals = ModelImporterNormals.None;
			modelImporter.importTangents = ModelImporterTangents.None;
		}

		void OnPostprocessMeshHierarchy(GameObject child) {
			if(!assetPath.StartsWith(importPath)) return;
			if(importAssets.Contains(assetPath)) return;
			//Debug.Log($"RePort.OnPostprocessMeshHierarchy({child.name})\nassetPath = {assetPath}");

			// TEMP: Explicitly break out supported models
			ParseModelName(assetPath, out _, out _, out var element, out var source, out _);
			if(importerDict.ContainsKey(source)) importerDict[source].ImportMeshHierarchy(child.transform, element);
		}

		void OnPostprocessModel(GameObject model) {
			if(!assetPath.StartsWith(importPath)) return;
			if(importAssets.Contains(assetPath)) return;
			//Debug.Log($"RePort.OnPostprocessModel({model.name})\nassetPath = {assetPath}");

			// Strip empty GameObjects
			// NOTE: Removing cameras applies to components, but not to their GameObjects
			RemoveEmpty(model);

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

		// TODO: Progress bar popup
		// FIXME: Check for PIM repeated calls after registration removal... and then prevent it!

		static void ProcessImportedModels() {
			// Ensure that ProcessImportedModels is called only once per import batch
			// IMPORTANT: Unregistering must occur before any possible exception to prevent lockup.
			EditorApplication.update -= ProcessImportedModels;

			// PROBLEM: Unsubscribing from EditorApplication.update is not immediate - multiple callbacks may be received
			// SOLUTION: Check importAssets count and abort immediately if empty.
			if(importAssets.Count == 0) {
				Debug.LogWarning($"GLITCH: RePort.ProcessImportedModels()");
				return;
			}
			var modelPathList = new HashSet<string>(importAssets);
			importAssets.Clear();
			Debug.Log($"RePort.ProcessImportedModels()");

			// There are 3 import types to consider:
			// Partial models, which are in a subfolder of importPath and have a suffix
			// Complete models, which are in a subfolder of importPath and have no suffix
			// Assembled models, which are in the importPath folder
			var partialModels = new Dictionary<string, List<GameObject>>();
			var completeModels = new List<GameObject>();
			var assembledModels = new List<GameObject>();

			foreach(var modelPath in modelPathList) {
				Debug.Log($"RePort.ProcessImportedModels()\nmodelPath = {modelPath}");

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

			CombinePartial(partialModels, completeModels);
			AssembleComplete(completeModels, assembledModels);
			var configured = ConfigureAssembled(assembledModels);

			// IMPORTANT: importAssets must not be cleared until the import process is complete.
			// importAssets abort calls to OnPreprocessModel and OnPostprocessModel
			// in the case that a model is reimported by a method.
			importAssets.Clear();

			// If only one model was imported, open it
			if(configured.Count == 1) EditorSceneManager.OpenScene(configured[0], OpenSceneMode.Single);
		}

		/// <summary>
		/// Creates an indepedent prefab
		/// </summary>
		/// <remarks>
		/// All assets used by model are copied into an adjacent folder.
		/// ExtractAssets calls ImportTextures - there is no need to call it first.
		/// </remarks>
		static public GameObject ExtractAssets(string modelPath) {
			ImportTextures(modelPath);

			var modelPathRoot = modelPath.Substring(0, modelPath.LastIndexOf('.'));
			var prefabPath = modelPathRoot + ".prefab";

			// Create independent prefab - FIXME: Skip this instantiation... GatherAssets handles it
			var model = EP.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
			PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
			var prefab = PrefabUtility.SaveAsPrefabAsset(model, prefabPath);
			GatherAssets.ApplyTo(prefab, modelPathRoot);
			EP.Destroy(model);

			return prefab;
		}

		/// <summary>
		/// Extracts and remaps textures for use by materials
		/// </summary>
		/// <remarks>
		/// For the implementation of the "Extract Textures" button 
		/// in the "Materials" tab of the "Import Settings" Inspector panel, see:
		/// https://github.com/Unity-Technologies/UnityCsReference/
		/// Modules/AssetPipelineEditor/ImportSettings/ModelImporterMaterialEditor.cs
		/// private void ExtractTexturesGUI()
		/// </remarks>
		static public void ImportTextures(string modelPath) {
			var modelImporter = AssetImporter.GetAtPath(modelPath) as ModelImporter;
			if(modelImporter == null) return;

			// Extract textures
			var texturesPath = modelPath.Substring(0, modelPath.LastIndexOf('.')) + "/Textures";
			try {
				AssetDatabase.StartAssetEditing();
				modelImporter.ExtractTextures(texturesPath);
			} finally {
				AssetDatabase.StopAssetEditing();
			}
			AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

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
			}

			// TODO: Avoid the pop-up requesting to fix normalmap texture types (ideally by fixing)
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
