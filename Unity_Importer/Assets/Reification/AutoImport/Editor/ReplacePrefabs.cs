// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Reification {
	/// <summary>
	/// Replaces placeholder transforms with their corresponding prefabs
	/// </summary>
	/// <remarks>
	/// A placeholder is an empty transform with a name that identifies the corresponding prefab.
	/// Using placeholders, a model exporter can describe each referentially instantiated constituent as a
	/// separate model file, with the constituent becoming a prefab on import.
	/// If no prefab is found an empty prefab will be created with the expected path and name, 
	/// to ensure that the object correspondance is maintained.
	/// </remarks>
	public class ReplacePrefabs {
		const string menuItemName = "Reification/Replace Prefabs";
		const int menuItemPriority = 21;

		[MenuItem(menuItemName, validate = true, priority = menuItemPriority)]
		static private bool Validate() {
			if(Selection.gameObjects.Length == 0) return false;
			foreach(var gameObject in Selection.gameObjects) {
				var prefabAssetType = PrefabUtility.GetPrefabAssetType(gameObject);
				if(
					prefabAssetType == PrefabAssetType.MissingAsset ||
					prefabAssetType == PrefabAssetType.Model
				) return false;
			}
			return true;
		}

		[MenuItem(menuItemName, priority = menuItemPriority)]
		static private void Execute() {
			Undo.IncrementCurrentGroup();
			Undo.SetCurrentGroupName("Replace Prefabs");

			var selectionList = Selection.gameObjects;
			foreach(var selection in selectionList) ApplyTo(selection);
		}

		public static void ApplyTo(GameObject gameObject, string searchRoot = null) {
			if(searchRoot == null) {
				searchRoot = AssetDatabase.GetAssetOrScenePath(gameObject);
				searchRoot = searchRoot.Substring(0, searchRoot.LastIndexOf('/'));
			}
			using(var editScope = new EP.EditGameObject(gameObject)) {
				var editObject = editScope.editObject;
				ReplacePlaceholders(searchRoot, editObject);
			}
		}

		public static void SplitPathName(string pathname, out string path, out string name, out string type) {
			path = "";
			var path_split = pathname.LastIndexOf('/');
			if(-1 < path_split) path = pathname.Substring(0, path_split);
			// else path_split = -1

			var name_split = pathname.LastIndexOf('.');
			if(name_split < path_split + 1) name_split = pathname.Length;
			name = pathname.Substring(path_split + 1, name_split - (path_split + 1));

			type = "";
			if(name_split + 1 < pathname.Length) type = pathname.Substring(name_split + 1);
		}

		class CachedPrefab {
			public CachedPrefab(string guid) {
				var pathname = AssetDatabase.GUIDToAssetPath(guid);
				SplitPathName(pathname, out _path, out _name, out _type);
				_prefab = null;
			}

			public CachedPrefab(GameObject asset) {
				var pathname = AssetDatabase.GetAssetPath(asset);
				SplitPathName(pathname, out _path, out _name, out _type);
				_prefab = asset;
			}

			string _type;
			public string type => _type;

			string _name;
			public string name => _name;

			string _path;
			public string path => _path;

			GameObject _prefab;
			public GameObject prefab {
				get {
					if(_prefab == null) _prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{_path}/{_name}.{_type}");
					return _prefab;
				}
			}
		}

		static Dictionary<string, CachedPrefab> GetPrefabs(string searchRoot) {
			var prefabs = new Dictionary<string, CachedPrefab>();

			var prefabGUIDs = AssetDatabase.FindAssets("t:GameObject", new[] { searchRoot });
			foreach(var guid in prefabGUIDs) {
				var cached = new CachedPrefab(guid);
				if(cached.type != "prefab") continue;
				if(prefabs.ContainsKey(cached.name)) {
					Debug.LogWarning($"Repeated prefab key {cached.name} at {AssetDatabase.GUIDToAssetPath(guid)} and {prefabs[cached.name].path}");
					continue;
				}
				prefabs.Add(cached.name, cached);
			}

			return prefabs;
		}

		/// <summary>
		/// Name of prefab that replaces placeholder
		/// </summary>
		/// <remarks>
		/// Every '=' is removed from name in order to prevent attempted replacement during reimport
		/// In the imported model, names will have been made unique by appending a suffix,
		/// but uniqueness will have been determined when the prefab name was included.
		/// In order to ensure uniqueness when checking for replacements the prefab name will be retained.
		/// 
		/// WARNING: Collisions are still possible dues to the '=' removal. This can be prevented by
		/// ensuring that this character does not appear in the object or prefab name parts.
		/// </remarks>
		public static string ConfigureName(string name) => name.Trim('=').Replace('=', '-');

		// Model export generates meshes in world coordinates
		// In order to retain information, each prefab is replaced with a transformed tetrahedron
		static void ConfigurePrefab(Transform placeholder, CachedPrefab cached) {
			var prefab = (PrefabUtility.InstantiatePrefab(cached.prefab) as GameObject).transform;
			EP.SetParent(prefab, placeholder.parent);
			prefab.localPosition = placeholder.localPosition;
			prefab.localRotation = placeholder.localRotation;
			prefab.localScale = placeholder.localScale;
			prefab.name = placeholder.name;
		}

		static void ReplacePlaceholders(string prefabPath, GameObject gameObject) {
			Dictionary<string, CachedPrefab> prefabs = GetPrefabs(prefabPath);
			var children = gameObject.Children(true);
			foreach(var child in children) {
				// Do not modify existing child prefabs
				var childPrefab = PrefabUtility.GetNearestPrefabInstanceRoot(child);
				if(childPrefab != null && childPrefab != gameObject) continue;

				// Placeholder names are constructed as "prefab_name=object_name"
				var name_parts = child.name.Split('=');
				if(name_parts.Length == 1) continue;

				// Create an placeholder prefab that can be modified after import
				if(!prefabs.ContainsKey(name_parts[0])) {
					var placeholder = EP.Instantiate();
					placeholder.name = name_parts[0];
					var placeholderPath = prefabPath + "/" + placeholder.name + ".prefab";
					var placeholderAsset = PrefabUtility.SaveAsPrefabAsset(placeholder, placeholderPath);
					prefabs[name_parts[0]] = new CachedPrefab(placeholderAsset);
					EP.Destroy(placeholder);
					//Debug.Log($"Missing prefab in {gameObject.name} for {child.Path()} -> created placeholder");
				}

				ConfigurePrefab(child.transform, prefabs[name_parts[0]]);
				EP.Destroy(child);
			}
		}
	}
}
