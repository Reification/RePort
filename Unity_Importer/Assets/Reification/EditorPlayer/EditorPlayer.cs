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
		// then when running headles don't even both with that!

		// TEMP: Use this instead of !Application.isBatchMode for performace testing
		public static bool useEditorUndo {
			get {
				return !Application.isBatchMode;
			}
		}

		/// <summary>Destroy, with undo support if in editor</summary>
		public static void Destroy(Object destroy) {
			if(destroy == null) return;
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

		/// <summary>Instantiate, with prefab linking and undo support if in editor</summary>
		/// <remarks>
		/// When called with no argument this will create a new GameObject
		/// </remarks>
		public static GameObject Instantiate(GameObject instantiate = null) {
			GameObject gameObject = null;
			if(instantiate == null) {
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
				if(PrefabUtility.GetPrefabAssetType(instantiate) != PrefabAssetType.NotAPrefab) {
					gameObject = PrefabUtility.InstantiatePrefab(instantiate) as GameObject;
				} else {
					gameObject = Object.Instantiate(instantiate);
				}
				if(useEditorUndo) {
					Undo.RegisterCreatedObjectUndo(gameObject, "Instantiate(" + instantiate.name + ")");
				} else {
					EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
				}
#endif
			} else {
				gameObject = Object.Instantiate(instantiate);
			}
			return gameObject;
		}

		public static void SetParent(Transform child, Transform parent) {
			if(useEditorAction) {
#if UNITY_EDITOR
				if(useEditorUndo) {
					Undo.SetTransformParent(child, parent, "SetParent(" + child.name + "," + parent.name + ")");
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
		/// Enable editing of a GameObject, even if the object is a prefab
		/// </summary>
		/// <remarks>
		/// Unity2019.4: Prefab editing is limited to creation and update of components and children.
		/// Removal requires opening the prefab for editing.
		/// </remarks>
		public class EditGameObject : System.IDisposable {
			/// <summary>
			/// Editable instance of gameObject
			/// </summary>
			public GameObject editObject { get; private set; }

			GameObject gameObject;

			public EditGameObject(GameObject gameObject) {
				if(useEditorAction) {
#if UNITY_EDITOR
					switch(PrefabUtility.GetPrefabAssetType(gameObject)) {
					case PrefabAssetType.NotAPrefab:
						if(Application.isBatchMode) {
							EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
						} else {
							Undo.RecordObject(gameObject, $"Merge to {gameObject.name}");
						}
						editObject = gameObject;
						break;
					case PrefabAssetType.Regular:
					case PrefabAssetType.Variant:
						var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
						editObject = PrefabUtility.LoadPrefabContents(prefabPath);
						break;
					default:
						editObject = null;
						break;
					}
#endif
				} else {
					editObject = gameObject;
				}
				this.gameObject = gameObject;
			}

			public void Dispose() {
				if(editObject == null) return;

				if(useEditorAction) {
#if UNITY_EDITOR
					switch(PrefabUtility.GetPrefabAssetType(gameObject)) {
					case PrefabAssetType.NotAPrefab:
						if(Application.isBatchMode) {
							EditorSceneManager.SaveOpenScenes();
						} else {
							Undo.RecordObject(gameObject, $"Merge to {gameObject.name}");
							// User interaction - do not save
						}
						break;
					case PrefabAssetType.Regular:
					case PrefabAssetType.Variant:
						var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
						PrefabUtility.SaveAsPrefabAsset(editObject, prefabPath);
						PrefabUtility.UnloadPrefabContents(editObject);
						break;
					}
#endif
				}
				editObject = null;
			}
		}

		/// <summary>
		/// Copies a GameObject without losing child prefab links
		/// </summary>
		/// <remarks>
		/// Unity2019.4: GameObject.Instantiate completely unpacks prefab links.
		/// </remarks>
		public class CopyGameObject : System.IDisposable {
			/// <summary>
			/// Copy of GameObject with prefab links sustained
			/// </summary>
			public GameObject copyObject { get; private set; }
			GameObject gameObject;
			string tempPath;

			public CopyGameObject(GameObject gameObject) {
				if(useEditorAction) {
#if UNITY_EDITOR
					GameObject sourceAsset = null;
					if(PrefabUtility.GetPrefabAssetType(gameObject) == PrefabAssetType.NotAPrefab) {
						tempPath = AssetDatabase.GenerateUniqueAssetPath("Assets/" + gameObject.name + ".prefab");
						sourceAsset = PrefabUtility.SaveAsPrefabAsset(gameObject, tempPath);
						PrefabUtility.UnpackPrefabInstance(copyObject, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
						copyObject.name = gameObject.name;
					} else {
						var sourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
						sourceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
						copyObject = PrefabUtility.InstantiatePrefab(sourceAsset) as GameObject;
					}
					copyObject.name = gameObject.name;
					// QUESTION: Do transform parameters also need to be copied?
#endif
					this.gameObject = gameObject;
				} else {
					copyObject = GameObject.Instantiate(gameObject);
				}
			}

			public void Dispose() {
				if(copyObject == null) return;

				if(useEditorAction) {
#if UNITY_EDITOR
					GameObject.DestroyImmediate(copyObject);
					if(PrefabUtility.GetPrefabAssetType(gameObject) == PrefabAssetType.NotAPrefab) {
						AssetDatabase.DeleteAsset(tempPath);
					}
#endif
				} else {
					GameObject.Destroy(copyObject);
				}
				copyObject = null;
			}
		}

		/// <summary>
		/// Create a transient directory for editor or player
		/// </summary>
		/// <remarks>
		/// Data will be created in the StreamingAssets folder, which is in Assets
		/// while using the Editor, and in the package files when using a runtime.
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
		/// The asset name will be the name of the asset object.
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
		static public T CopyAssetToPath<T>(T asset, string assetPath, string assetSuffix = null) where T : Object {
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

				if(AssetDatabase.IsSubAsset(asset)) {
					// Extract asset at new path
					AssetDatabase.ExtractAsset(asset, newPath);
				} else {
					// Copy asset to new path
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
						var oldPath = AssetDatabase.GetAssetPath(asset);
						if(oldPath != null && oldPath.Length > 0) {
							// IMPORTANT: CopyAsset will retain file type for image and model assets
							AssetDatabase.CopyAsset(oldPath, newPath);
						} else {
							// NOTE: CreateAsset will fail when object already references an asset
							AssetDatabase.CreateAsset(asset, newPath);
						}
					}
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
