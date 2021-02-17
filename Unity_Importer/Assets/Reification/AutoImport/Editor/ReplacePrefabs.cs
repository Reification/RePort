// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Reification {
	/// <summary>
	/// Replaces placeholder meshes with the corresponding prefabs
	/// </summary>
	/// <remarks>
	/// A placeholder mesh is a tetrahedra whose vertices describe the general linear transform
	/// that will be applied to the prefab instance that will replace it.
	/// The transform derivation assumes 4 vertices, in a chiral order that is consistent with Unity.
	/// </remarks>
	public class ReplacePrefabs {
		const string menuItemName = "Reification/Replace Prefabs";
		const int menuItemPriority = 20;

		// IMPORTANT: It should be possible to apply this directly to prefabs
		// NOTE: There is some risk of recursion - hopefully that throws an error.

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

		static public void ApplyTo(GameObject gameObject, string searchRoot = null) {
			if(searchRoot == null) {
				searchRoot = AssetDatabase.GetAssetOrScenePath(gameObject);
				searchRoot = searchRoot.Substring(0, searchRoot.LastIndexOf('/'));
			}
			using(var editScope = new EP.EditGameObject(gameObject)) {
				var editObject = editScope.editObject;
				ReplacePlaceholders(searchRoot, editObject);
			}
		}

		static public void SplitPathName(string pathname, out string path, out string name, out string type) {
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
		/// In order to ensure uniqueness when check for replacements, this the prefab name will be retaining.
		/// 
		/// WARNING: Collisions are still possible dues to the '=' removal. This can be prevented by
		/// ensuring that this character does not appear in the object or prefab name parts.
		/// </remarks>
		static public string ConfigureName(GameObject gameObject) => gameObject.name.Trim('=').Replace('=', '-');

		// Model export generates meshes in world coordinates
		// In order to retain information, each prefab is replaced with a transformed tetrahedron
		static void ConfigurePrefab(Transform placeholder, CachedPrefab cached) {
			// Validate locator
			// WARNING: If a tetrahedron is imported with ModelImporter.meshOptimizationFlags != 0
			// the vertex array can be reordered, and vertices may be replicated up to 3 times
			// to support a normal vector for each assocaited triangle.
			var meshFilter = placeholder.GetComponent<MeshFilter>();
			if(!meshFilter) {
				Debug.Log($"ReplacePrefab({placeholder.Path()}) missing MeshFilter");
				return;
			}
			Mesh sharedMesh = meshFilter.sharedMesh;
			if(!sharedMesh) {
				Debug.Log($"ReplacePrefab({placeholder.Path()}) missing sharedMesh");
				return;
			}
			var vertices = sharedMesh.vertices;
			if(vertices == null || vertices.Length != 4) {
				Debug.Log($"ReplacePrefab({placeholder.Path()}) incorrect vertext count {vertices?.Length ?? 0}");
				return;
			}

			// Derive basis in world coordinates
			var origin = placeholder.TransformPoint(vertices[0]);
			var basisX = placeholder.TransformPoint(vertices[1]) - origin;
			var basisY = placeholder.TransformPoint(vertices[2]) - origin;
			var basisZ = placeholder.TransformPoint(vertices[3]) - origin;

			// TODO: Use SVD to construct transform, which can include shear
			// TEMP: Assume transform is axial scaling followed by rotation only
			// NOTE: The origin and bases are simply the columns of an affine (3x4) transform matrix
			var prefab = (PrefabUtility.InstantiatePrefab(cached.prefab) as GameObject).transform;
			prefab.name = placeholder.name;
			prefab.localScale = new Vector3(basisX.magnitude, basisY.magnitude, basisZ.magnitude);
			prefab.rotation = Quaternion.LookRotation(basisZ, basisY);
			prefab.position = origin;
			EP.SetParent(prefab, placeholder.parent);
		}

		static void ReplacePlaceholders(string prefabPath, GameObject gameObject) {
			Dictionary<string, CachedPrefab> prefabs = GetPrefabs(prefabPath);
			var children = gameObject.Children(true);
			foreach(var child in children) {
				var name_parts = child.name.Split('=');
				if(name_parts.Length == 1) continue;
				if(prefabs.TryGetValue(name_parts[name_parts.Length - 1], out var cached)) {
					// When reimporting retain previous replacement if present
					string replaceName = ConfigureName(child);
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
