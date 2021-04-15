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

			// FIXME: Only prefab assets should be included - FBX will have a similar name and should be ignored
			var prefabGUIDs = AssetDatabase.FindAssets("t:GameObject", new[] { searchRoot });
			foreach(var guid in prefabGUIDs) {
				var cached = new CachedPrefab(guid);
				if(cached.type != "prefab") continue;
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
				var name_parts = child.name.Split('=');
				if(name_parts.Length == 1) continue;
				if(prefabs.TryGetValue(name_parts[0], out var cached)) {
					// When reimporting retain previous replacement if present
					string replaceName = ConfigureName(child.name);
					var replaceList = child.transform.parent.NameFindInChildren(replaceName);
					// ASSUME: Each placeholder yields a unique configured name
					if(replaceList.Length == 0) {
						child.name = replaceName;
						ConfigurePrefab(child.transform, cached);
					}
					EP.Destroy(child);
				} else {
					Debug.LogWarning($"Missing prefab for {gameObject.name} place holder {child.Path()}");
				}
			}
		}
	}
}
