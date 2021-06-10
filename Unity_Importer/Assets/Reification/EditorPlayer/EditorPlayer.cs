// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace Reification {
	/// <summary>Equivalent actions that must be performed differently in editor or player modes</summary>
	/// <remarks>
	/// The editor / player dichotomy actually has additional cases to be considered
	/// - The player may be launched from the editor, in which case player actions should be used
	/// - The editor may be launched in batch mode, in which case Undo tracking is not needed
	/// - Assets types may require different actions, or may be immutable
	/// </remarks>
	public static class EP {
		/// <summary>Should editor action be used?</summary>
		/// <remarks>
		/// This is used to avoid taking editor actions while editor is playing.
		/// This will return false if when not called in editor,
		/// so calls made when true can be enclosed in #if UNITY_EDITOR... #endif
		/// </remarks>
		public static bool useEditorAction {
			get {
#if UNITY_EDITOR
				return !EditorApplication.isPlaying;
#else
				return false;
#endif
			}
		}

		// IDEA: Use RAII to manage isUndoRequired since anything touching assets
		// will have no undo option. However, leaving this in batch-mode would be bad.
		// NOTE: Just use a static counter for number of -> batch requests
		// TODO: Handle: AssetDatabase.StartAssetEditing() within this too.
		// NOTE: Already an enum for this: InteractionMode.AutomatedAction
		// TODO: The editing mode needs a suspension so that objects can be instantiated
		// temporarily, without marking a scene as dirty or adding to undo.
		// IDEA: Use RAII to push & pop states. Also, when running headless never
		// enter the Undo recording state.
		// QUESTION: Can a scene be saved from a script if not marked "dirty"? If so,
		// then when running headless, marking dirty is unnecessary

		// TEMP: Use this instead of !Application.isBatchMode for performace testing
		public static bool useEditorUndo {
			get {
				return !Application.isBatchMode;
			}
		}

		/// <summary>
		/// Get GameObject type with respect to asset database
		/// </summary>
		/// <remarks>
		/// If Connected or Persistent, use PrefabUtility.GetPrefabAssetType(gameObject) to distinguish model/prefab/variant types.
		/// If Connected PrefabUtility.GetNearestPrefabInstanceRoot(gameObject) == gameObject to distinguish root from child. 
		/// If Persistent use !!gameObject.transform.parent to distinguish root from child (path only identifies root).
		/// </remarks>
		public enum GameObjectType {
			Nothing, // GameObject is null / destroyed
			Instance, // Non-prefab instance in scene
			Connected, // Instantiated prefab root or child
			Persistent // Uninstantiated prefab root or child
		}

		public static GameObjectType GetGameObjectType(GameObject gameObject) {
			if(!gameObject) return GameObjectType.Nothing;

			if(useEditorAction) {
#if UNITY_EDITOR
				// Non-empty when gameObject is an asset
				// NOTE: if(prefab) assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prefab)
				var assetPath = AssetDatabase.GetAssetPath(gameObject);
				if(assetPath.Length > 0) return GameObjectType.Persistent;

				// Non-null when gameObject is an instantiated prefab
				var prefab = PrefabUtility.GetNearestPrefabInstanceRoot(gameObject);
				if(prefab && !PrefabUtility.IsAddedGameObjectOverride(gameObject)) return GameObjectType.Connected;
#endif
			}
			return GameObjectType.Instance;
		}

		/// <summary>Destroy, with undo support if in editor</summary>
		public static void Destroy(Object destroy) {
			if(destroy == null) return;
			// FIXME: Check if object is actually asset and don't destroy

			if(useEditorAction) {
#if UNITY_EDITOR
				if(useEditorUndo) {
					Undo.DestroyObjectImmediate(destroy);
				} else {
					Object.DestroyImmediate(destroy);
					EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
				}
#endif
			} else {
				Object.Destroy(destroy);
			}
		}

#if UNITY_EDITOR
		// WARNING: If there is a name override, PathName will not resolve!
		// TODO: Find a way to clone prefab with overrides intact.
		// QUESTION: Is there a way to accomplish instatiation using object serializations?
		// Ideally, this would handle the connection and override persistence.
		// https://docs.unity3d.com/ScriptReference/SerializedObject.html
		// Construct, then iterate & copy?
		static GameObject InstantiateChild(GameObject original, GameObject parent) {
			GameObject child = null;
			// IMPORTANT: PrefabUtility.InstantiatePrefab applies only to assets, not to instances
			// IMPORTANT: PrefabUtility.GetCorrespondingObjectFromSource applies only to instances, not to assets
			var asset = PrefabUtility.GetCorrespondingObjectFromSource(parent);
			GameObject copy_asset = null;
			if(asset) copy_asset = asset;
			else copy_asset = original;
			var copy = PrefabUtility.InstantiatePrefab(copy_asset) as GameObject;
			if(parent != original) {
				var path = new PathName(original, parent);
				var find = path.Find(copy.transform);
				if(find.Length == 1) {
					child = find[0].gameObject;
					// Unpack to enable orphaning, only once since nearest root was instantiated
					var unpack = PrefabUtility.GetOutermostPrefabInstanceRoot(child);
					while(unpack) {
						PrefabUtility.UnpackPrefabInstance(unpack, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
						unpack = PrefabUtility.GetOutermostPrefabInstanceRoot(child);
					}
					child.transform.SetParent(null);
				}
				EP.Destroy(copy);
			} else {
				child = copy;
			}
			return child;
		} 
#endif

		/// <summary>Instantiate, with prefab linking and undo support</summary>
		/// <remarks>
		/// When called with no argument this will create a new GameObject
		/// </remarks>
		public static GameObject Instantiate(GameObject original = null) {
			GameObject gameObject = null;
			if(original == null) {
				gameObject = new GameObject();
				if(useEditorAction) {
#if UNITY_EDITOR
					if(useEditorUndo) {
						Undo.RegisterCreatedObjectUndo(gameObject, "Create GameObject()");
					} else {
						EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
					}
#endif
				}
				return gameObject;
			}
			if(useEditorAction) {
#if UNITY_EDITOR
				switch(GetGameObjectType(original)) {
				case GameObjectType.Instance: {
						// PROBLEM: GameObject.Instantiate(original) will not retain prefab links.
						// SOLUTION: Make the object into a prefab and then unpack.
						var tempPath = AssetDatabase.GenerateUniqueAssetPath("Assets/" + original.name + ".prefab");
						var tempAsset = PrefabUtility.SaveAsPrefabAsset(original, tempPath);
						gameObject = PrefabUtility.InstantiatePrefab(tempAsset) as GameObject;
						PrefabUtility.UnpackPrefabInstance(gameObject, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
						AssetDatabase.DeleteAsset(tempPath);
						break;
					}
				case GameObjectType.Connected: {
						var parent = PrefabUtility.GetNearestPrefabInstanceRoot(original);
						gameObject = InstantiateChild(original, parent);
						break;
					}
				case GameObjectType.Persistent: {
						var parent = original.transform.root.gameObject;
						gameObject = InstantiateChild(original, parent);
						break;
					}
				default: // GameObjectType.Nothing
					gameObject = new GameObject();
					break;
				}
				if(useEditorUndo) {
					Undo.RegisterCreatedObjectUndo(gameObject, "Instantiate GameObject(" + gameObject.name + ")");
				} else {
					EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
				}
#endif
			} else {
				if(original) gameObject = GameObject.Instantiate(original);
				else gameObject = new GameObject();
			}
			return gameObject;
		}

		public static void SetParent(Transform child, Transform parent) {
			if(!child) return;

			if(useEditorAction) {
#if UNITY_EDITOR
				if(useEditorUndo) {
					Undo.SetTransformParent(child, parent, "SetParent(" + child.name + "," + parent.name + ")");

					if(child.parent != parent) {
						var parent_in_prefab = PrefabUtility.IsPartOfPrefabAsset(parent);
						var child_in_prefab = PrefabUtility.IsPartOfPrefabAsset(child);
						var child_type = PrefabUtility.GetPrefabAssetType(child);

						Debug.LogError("Reparenting failed...");
						child.SetParent(parent);
						if(child.parent != parent) {
							Debug.LogError("Reparenting failed again...");
						}
					}

				} else {
					child.SetParent(parent);
					EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
				}
#endif
			} else {
				child.SetParent(parent);
			}
		}

		public static T AddComponent<T>(GameObject gameObject) where T : Component {
			T component = default;
			if(useEditorAction) {
#if UNITY_EDITOR
				if(useEditorUndo) {
					component = Undo.AddComponent<T>(gameObject);
				} else {
					component = gameObject.AddComponent<T>();
					EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
				}
#endif
			} else {
				component = gameObject.AddComponent<T>();
			}
			return component;
		}

		/// <summary>
		/// Enable editing of a GameObject, even if the object is a prefab or is in a prefab
		/// </summary>
		/// <remarks>
		/// Unity2019.4: Prefab editing is limited to creation and update of components and children.
		/// Removal requires opening the prefab for editing.
		/// When an object is a child of a prefab, the nearest parent prefab will be edited instead of
		/// defining overrides or creating a variant.
		/// WARNING: This will result in missing overrides on the editObject.
		/// WARNING: If gameObject name has been changed editObject may be null,
		/// and if path is not unique editObject may not correspond.
		/// </remarks>
		public class EditGameObject : System.IDisposable {
			/// <summary>
			/// Editable instance of gameObject
			/// </summary>
			public GameObject editObject { get; private set; }

			public GameObjectType editObjectType { get; private set; } = GameObjectType.Nothing;
			public string editAssetPath { get; private set; } = "";
			GameObject editPrefab;

			public EditGameObject(GameObject gameObject) {
				if(useEditorAction) {
#if UNITY_EDITOR
					editObjectType = GetGameObjectType(gameObject);
					switch(editObjectType) {
					case GameObjectType.Persistent: {
							// Load as PreviewScene Object 
							editAssetPath = AssetDatabase.GetAssetPath(gameObject);
							editPrefab = PrefabUtility.LoadPrefabContents(editAssetPath);
							editObject = editPrefab;
							break;
						}
					case GameObjectType.Connected: {
							// Path to gameObject relative to prefab
							// NOTE: EditorSceneManager.IsPreviewSceneObject(editPrefab) == true
							var prefab = PrefabUtility.GetNearestPrefabInstanceRoot(gameObject);
							var path = new PathName(gameObject, prefab);

							// Instantiate a copy of prefab and locate copy of gameObject
							var asset = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
							editAssetPath = AssetDatabase.GetAssetPath(asset);
							editPrefab = PrefabUtility.InstantiatePrefab(asset) as GameObject;
							var editObjectList = path.Find(editPrefab.transform);
							if(editObjectList.Length == 1) editObject = editObjectList[0].gameObject;
							break;
						}
					case GameObjectType.Instance:
						editObject = gameObject;
						break;
					}

					if(Application.isBatchMode) {
						EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
					} else {
						// QUESTION: Does this work for prefabs?
						Undo.RecordObject(gameObject, $"Edit {gameObject.name}");
					}
#endif
				} else {
					editObject = gameObject;
				}
			}

			public void Dispose() {
				if(editObject == null) return;

				if(useEditorAction) {
#if UNITY_EDITOR
					switch(editObjectType) {
					case GameObjectType.Persistent: {
							PrefabUtility.SaveAsPrefabAsset(editPrefab, editAssetPath);
							PrefabUtility.UnloadPrefabContents(editPrefab);
							break;
						}
					case GameObjectType.Connected: {
							PrefabUtility.ApplyPrefabInstance(editPrefab, InteractionMode.AutomatedAction);
							Object.DestroyImmediate(editPrefab);
							break;
						}
					case GameObjectType.Instance: {
							if(Application.isBatchMode) {
								EditorSceneManager.SaveOpenScenes();
							}
							// else: User interaction - do not save
							break;
						}
					}
#endif
				}
				editObject = null;
			}
		}

		/// <summary>
		/// Create a transient directory for editor or player
		/// </summary>
		/// <remarks>
		/// Data will be created in the StreamingAssets folder, which is in Assets
		/// while using the Editor, and in the package files while in the case of a build.
		/// </remarks>
		/// <param name="path">relative path using "/" directory separators</param>
		/// <param name="create">when false count directories to be created, without creating them</param>
		/// <returns>count of created folders in path</returns>
		public static int CreateStreamingPath(string path, bool create = true) {
			var created = 0;
			if(useEditorAction) {
#if UNITY_EDITOR
				// PROBLEM: AssetDatabase.IsValidFolder may *sometimes* return false 
				// for a folder that has been created previously in the same import session!
				// NOTE: AssetDatabase.Refresh() does not resolve this
				// SOLUTION: Use Directory.Exists test instead.
				var last = "Assets/StreamingAssets";
				var lastOS = Application.streamingAssetsPath;
				var folders = path.Split('/');
				foreach(var folder in folders) {
					var next = last + "/" + folder;
					var nextOS = lastOS + Path.DirectorySeparatorChar + folder;
					if(!Directory.Exists(nextOS)) {
						if(create) AssetDatabase.CreateFolder(last, folder);
						created += 1;
					}
					last = next;
					lastOS = nextOS;
				}
#endif
			} else {
				var pathOS = Path.Combine(Application.streamingAssetsPath, path.Replace('/', Path.DirectorySeparatorChar));
				Directory.CreateDirectory(pathOS);
			}
			return created;
		}

		/// <summary>
		/// Create a persistent directory for editor or player
		/// </summary>
		/// <remarks>
		/// Editor data in this path will be created in Assets directory
		/// Player data in this path will persist across application release updates
		/// </remarks>
		/// <param name="path">relative path using "/" directory separators</param>
		/// <param name="create">when false count directories to be created, without creating them</param>
		/// <returns>count of created folders in path</returns>
		public static int CreatePersistentPath(string path, bool create = true) {
			var created = 0;
			if(useEditorAction) {
#if UNITY_EDITOR
				// PROBLEM: AssetDatabase.IsValidFolder may *sometimes* return false 
				// for a folder that has been created previously in the same import session!
				// NOTE: AssetDatabase.Refresh() does not resolve this
				// SOLUTION: Use Directory.Exists test instead.
				var last = "Assets";
				var lastOS = Application.dataPath.Replace('/', Path.DirectorySeparatorChar);
				var folders = path.Split('/');
				foreach(var folder in folders) {
					var next = last + "/" + folder;
					var nextOS = lastOS + Path.DirectorySeparatorChar + folder;
					if(!Directory.Exists(nextOS)) {
						if(create) AssetDatabase.CreateFolder(last, folder);
						created += 1;
					}
					last = next;
					lastOS = nextOS;
				}
#endif
			} else {
				var pathOS = Path.Combine(Application.persistentDataPath, path.Replace('/', Path.DirectorySeparatorChar));
				Directory.CreateDirectory(pathOS);
			}
			return created;
		}

		/// <summary>
		/// Copies asset of type T to specified persistent path
		/// </summary>
		/// <remarks>
		/// If the specified path does not exist, it will be created using EP.CreatePersistentPath.
		/// The asset name will be a unique derivative of the name of the asset object.
		/// If assetSuffix is not specified the suffix corresponding to the asset type will be used.
		/// 
		/// WARNING: When AssetDatabase.StartAssetEditing() has been called, AssetDatabase.CopyAsset
		/// and PrefabUtility.SaveAsPrefabAsset will not modify AssetDatabase, in which case the returned
		/// asset reference will be the asset argument.
		/// </remarks>
		/// <typeparam name="T">Asset type</typeparam>
		/// <param name="asset">Asset to be copied</param>
		/// <param name="assetPath">Path relative to persistent folder where asset will be created</param>
		/// <param name="assetSuffix">Optional override of default suffix for asset type</param>
		/// <returns>The asset copy, or the origin of AssetDatabase has not been updated</returns>
		public static T CopyAssetToPath<T>(T asset, string assetPath, string assetSuffix = null) where T : Object {
			// Ensure that the asset path folders exist
			EP.CreatePersistentPath(assetPath);
			T assetCopy = null;

			if(useEditorAction) {
#if UNITY_EDITOR
				// Match asset and file type, and ensure that asset will be unique
				if(assetSuffix == null) {
					assetSuffix = ".asset";
					var oldPath = AssetDatabase.GetAssetPath(asset);
					if(oldPath != null) {
						// Preserve existing file type (important for images and models)
						assetSuffix = oldPath.Substring(oldPath.LastIndexOf('.'));
					} else {
						// Use default file type for asset type
						// https://docs.unity3d.com/ScriptReference/AssetDatabase.CreateAsset.html
						if(asset is GameObject) assetSuffix = ".prefab";
						if(asset is Material) assetSuffix = ".mat";
						if(asset is Cubemap) assetSuffix = ".cubemap";
						if(asset is GUISkin) assetSuffix = ".GUISkin";
						if(asset is Animation) assetSuffix = ".anim";
						// TODO: Cover other specialized types such as giparams
					}
				}
				var newPath = "Assets/" + assetPath + "/" + asset.name + assetSuffix;
				newPath = AssetDatabase.GenerateUniqueAssetPath(newPath);
				// WARNING: (Unity2019.4 undocumented) AssetDatabase.GenerateUniqueAssetPath will trim name spaces

				// Copy asset to new path
				// NOTE: The goal is to keep the original asset in place, 
				// so AssetDatabase.ExtractAsset is not used.
				if(asset is GameObject) {
					var gameObject = asset as GameObject;
					if(PrefabUtility.GetPrefabAssetType(gameObject) == PrefabAssetType.NotAPrefab) {
						assetCopy = PrefabUtility.SaveAsPrefabAsset(gameObject, newPath) as T;
					} else {
						// NOTE: PrefabUtility.SaveAsPrefabAsset cannot save an asset reference as a prefab
						var copyObject = PrefabUtility.InstantiatePrefab(gameObject) as GameObject;
						// NOTE: Root must be unpacked, otherwise this will yield a prefab variant
						PrefabUtility.UnpackPrefabInstance(copyObject, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
						assetCopy = PrefabUtility.SaveAsPrefabAsset(copyObject, newPath) as T;
						EP.Destroy(copyObject);
					}
				} else {
					// IMPORTANT: SubAssets must be instantiated, otherise AssetDatabase.CreateAsset(asset, newPath) will fail 
					// with error: "Couldn't add object to asset file because the Mesh [] is already an asset at [].fbx"
					// NOTE: AssetDatabase.ExtractAsset will register as a modification of model import settings
					if(AssetDatabase.IsSubAsset(asset)) AssetDatabase.CreateAsset(Object.Instantiate(asset), newPath);
					else AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(asset), newPath);
				}
				assetCopy = AssetDatabase.LoadAssetAtPath<T>(newPath);
#endif
			} else {
				Debug.LogWarning("Runtime asset copying is not implemented");
				// TODO: For runtime version simply copy the file!
			}

			if(!assetCopy) return asset;  // Continue to use asset at previous path
			return assetCopy as T;
		}

		// TODO: CreateScene has editor and runtime versions
		// https://docs.unity3d.com/ScriptReference/SceneManagement.SceneManager.CreateScene.html
		// https://docs.unity3d.com/ScriptReference/SceneManagement.EditorSceneManager.NewScene.html

		// TODO: Could import asset bundles at runtime
		// https://docs.unity3d.com/ScriptReference/AssetBundle.LoadFromFile.html
		// https://docs.unity3d.com/ScriptReference/AssetBundle.GetAllLoadedAssetBundles.html
	}
}
