// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Reification {
	public class AutoScene {
		const string menuItemName = "Reification/Auto Scene";
		const int menuItemPriority = 30;

		[MenuItem(menuItemName, validate = true, priority = menuItemPriority)]
		static private bool Validate() {
			if(Selection.gameObjects.Length == 0) return false;
			foreach(var gameObject in Selection.gameObjects) {
				var assetType = PrefabUtility.GetPrefabAssetType(gameObject);
				if(
					assetType == PrefabAssetType.Model ||
					assetType == PrefabAssetType.NotAPrefab
				) return false;
			}
			return true;
		}

		[MenuItem(menuItemName, priority = menuItemPriority)]
		static private void Execute() {
			Undo.IncrementCurrentGroup();
			Undo.SetCurrentGroupName("Auto Scene");

			// First selected object determines scene path
			var path = AssetDatabase.GetAssetOrScenePath(Selection.gameObjects[0]);
			path = path.Substring(0, path.LastIndexOf('.'));

			var scenePath = ApplyTo(path, Selection.gameObjects);

			// View created scene
			EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
		}

		static public string ApplyTo(string path, params GameObject[] gameObjects) {
			var scenePath = CreateScene(path, gameObjects);
			// IMPORTANT: Until the scene has been imported physics raycasts
			// required for object placement will not hit colliders
			AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
			AddDefaults(scenePath);
			// IMPORTANT: Changes made to scene aftyer save but before import 
			// will overwrite current changes unless scene is imported.
			AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
			return scenePath;
		}

		static public string CreateScene(string path, params GameObject[] gameObjects) {
			// PROBLEM: When using NewSceneMode.Single during import assertion "GetApplication().MayUpdate()" fails
			// SOLUTION: During import, using Additive loading works.
			// PROBLEM: InvalidOperationException: Cannot create a new scene additively with an untitled scene unsaved.
			// NOTE: This can occur when the previously opened scene has ceased to exist, in particular when a project is opened.
			var scene = EditorSceneManager.GetActiveScene();
			var addNew = scene.name.Length > 0 && scene.path.Length > 0; // scene.IsValid() will be true even when path and name are empty
			if(addNew) {
				scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
			} else {
				// Remove all default scene objects
				foreach(var rootObject in scene.GetRootGameObjects()) EP.Destroy(rootObject);
			}
			EditorSceneManager.SetActiveScene(scene);

			// Add objects to scene
			foreach(var gameObject in gameObjects) EP.Instantiate(gameObject);
			// PROBLEM: If scene is created during asset import physics computations will not be initialized

			// PROBLEM: At end of import the open scene will have been modified, so a pop-up will appear.
			// SOLUTION: After loading the scene in additive mode, close it.
			var scenePath = path + ".unity";
			EditorSceneManager.SaveScene(scene, scenePath);
			if(addNew) EditorSceneManager.CloseScene(scene, true);
			return scenePath;
		}

		static public void AddDefaults(string scenePath) {
			var sceneSetup = EditorSceneManager.GetSceneManagerSetup();
			try {
				// IMPORTANT: Lightmapping.Bake() should be called when only scenes contributing to bake are open
				var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

				// Get scene bounds
				// TODO: Move this to bounds extension.
				var emptyBounds = true;
				var sceneBounds = new Bounds();
				foreach(var sceneObject in scene.GetRootGameObjects()) {
					AutoLightProbes.ApplyTo(sceneObject);
					// TODO: Hook for Reflection probes, Acoustic probes...
					// NOTE: AutoLights modifies imported values, but the prefab will retain originals.
					// IDEA: Use refelction to create a configuration hooks in ModelImport.

					var objectRenderers = sceneObject.GetComponentsInChildren<Renderer>();
					foreach(var renderer in objectRenderers) {
						if(emptyBounds) {
							sceneBounds = renderer.bounds;
							emptyBounds = false;
						} else {
							sceneBounds.Encapsulate(renderer.bounds);
						}
					}
				}

				CreateSun(sceneBounds);
				CreatePlayer(sceneBounds);
				// TODO: Use IConfigurable to configure objects in scene.
				// TODO: When Player and Sun can be configured the can be input as arguments by RePort.

				EditorSceneManager.SaveScene(scene);
			} finally {
				EditorSceneManager.RestoreSceneManagerSetup(sceneSetup);
			}
		}

		public const string sunPrefabPath = "Assets/Reification/AutoImport/Prefabs/Sun.prefab";

		static void CreateSun(Bounds sceneBounds) {
			var sunAsset = AssetDatabase.LoadAssetAtPath<GameObject>(sunPrefabPath);
			if(!sunAsset) {
				Debug.LogWarning($"Missing asset: {sunPrefabPath}");
				return;
			}
			var sun = EP.Instantiate(sunAsset);

			var light = sun.GetComponentInChildren<Light>();
			RenderSettings.sun = light;

			// Position the sun source outside of the model
			// NOTE: This could be managed by a configuration component
			sun.transform.position = sceneBounds.center;
			light.transform.localPosition = new Vector3(0f, 0f, -sceneBounds.extents.magnitude);
		}

		public const string playerPrefabPath = "Assets/Reification/AutoImport/Prefabs/Player.prefab";

		static public float playerPositionStep = 1f; // meters

		static void CreatePlayer(Bounds sceneBounds) {
			var playerAsset = AssetDatabase.LoadAssetAtPath<GameObject>(playerPrefabPath);
			if(!playerAsset) {
				Debug.LogWarning($"Missing asset: {playerPrefabPath}");
				return;
			}
			var player = EP.Instantiate(playerAsset);

			// Place the player
			// NOTE: This could be managed by a configuration component
			var position = Vector3.zero;
			position.y = sceneBounds.max.y + playerPositionStep;
			for(position.x = sceneBounds.min.x + playerPositionStep; position.x < sceneBounds.max.x - playerPositionStep; position.x += playerPositionStep) {
				for(position.z = sceneBounds.min.z + playerPositionStep; position.z < sceneBounds.max.z - playerPositionStep; position.z += playerPositionStep) {
					if(Physics.Raycast(position, Vector3.down, out var hitInfo, sceneBounds.size.y + playerPositionStep * 2f)) {
						player.transform.position = hitInfo.point;
						position = sceneBounds.max; // Break from both loops
					}
				}
			}
		}
	}
}
