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
				ReplaceMatchedPrefabs(searchRoot, editObject);
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


		// Model export generates meshes in world coordinates
		// In order to retain information, each prefab is replaced with a transformed tetrahedron
		static void ReplacePrefab(Transform locator, CachedPrefab cached) {
			// Validate locator
			// WARNING: If a tetrahedron is imported with ModelImporter.meshOptimizationFlags != 0
			// the vertex array can be reordered, and vertices may be replicated up to 3 times
			// to support a normal vector for each assocaited triangle.
			var meshFilter = locator.GetComponent<MeshFilter>();
			var sharedMesh = meshFilter?.sharedMesh;
			var vertices = sharedMesh?.vertices;
			if(vertices == null || vertices.Length != 4) return;

			// Derive basis in world coordinates
			var origin = locator.TransformPoint(vertices[0]);
			var basisX = locator.TransformPoint(vertices[1]) - origin;
			var basisY = locator.TransformPoint(vertices[2]) - origin;
			var basisZ = locator.TransformPoint(vertices[3]) - origin;

			// TODO: Use SVD to construct transform, which can include shear
			// TEMP: Assume transform is axial scaling followed by rotation only
			// NOTE: The origin and bases are simply the columns of an affine (3x4) transform matrix
			var prefab = (PrefabUtility.InstantiatePrefab(cached.prefab) as GameObject).transform;
			if(locator.name.Length > 0) prefab.name = locator.name;
			prefab.localScale = new Vector3(basisX.magnitude, basisY.magnitude, basisZ.magnitude);
			prefab.rotation = Quaternion.LookRotation(basisZ, basisY);
			prefab.position = origin;
			EP.SetParent(prefab, locator.parent);
			// IMPORTANT: place meshes defined world coordinates

			EP.Destroy(locator.gameObject);
		}

		static void ReplaceMatchedPrefabs(string prefabPath, GameObject gameObject) {
			Dictionary<string, CachedPrefab> prefabs = GetPrefabs(prefabPath);
			var children = gameObject.transform.Children(true);
			foreach(var child in children) {
				var name_parts = child.name.Split('=');
				if(name_parts.Length == 1) continue;
				// TODO: String _# suffix added by FBX names (do this on import)
				// If name_parts[1] is non-zero (after stripping) use that, else use name_parts[0]
				if(prefabs.TryGetValue(name_parts[0], out var cached)) ReplacePrefab(child.transform, cached);
				else Debug.LogWarning($"Missing prefab for {gameObject.name} place holder {child.Path()}");
			}
		}
	}
}
