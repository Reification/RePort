// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using UnityEngine;
using UnityEditor;

namespace Reification {
	/// <summary>
	/// Configure all GameObjects and Components in hierarchy to be static
	/// </summary>
	/// <remarks>
	/// Components that could make object dynamic will be removed.
	/// </remarks>
	public class AutoStatic {
		const string menuItemName = "Reification/Auto Static";
		const int menuItemPriority = 24;

		[MenuItem(menuItemName, validate = true, priority = menuItemPriority)]
		static bool Validate() {
			return Selection.gameObjects.Length > 0;
		}

		[MenuItem(menuItemName, priority = menuItemPriority)]
		static void Execute() {
			Undo.IncrementCurrentGroup();
			Undo.SetCurrentGroupName("Auto Static");

			var selectionList = Selection.gameObjects;
			foreach(var selection in selectionList) ApplyTo(selection);
		}

		/// <summary>
		/// Recursively configure all objects and components in hierarchy to be static
		/// </summary>
		/// <param name="gameObject">Root of hierarchy to be converted to static configuration</param>
		/// <param name="prefabs">Apply static conversion overrides to prefabs in hierarchy</param>
		static public void ApplyTo(GameObject gameObject, bool prefabs = false) {
			using(var editScope = new EP.EditGameObject(gameObject)) {
				var editObject = editScope.editObject;

				// Set static configurations
				GameObjectSetStatic(editObject);
				foreach(var renderer in editObject.GetComponents<Renderer>()) RendererSetStatic(renderer);
				foreach(var collider in editObject.GetComponents<Collider>()) ColliderSetStatic(collider);
				foreach(var light in editObject.GetComponents<Light>()) LightSetStatic(light);

				// Remove dynamic components
				foreach(var monoBehavior in editObject.GetComponents<MonoBehaviour>()) EP.Destroy(monoBehavior);
				foreach(var physics in editObject.GetComponents<Rigidbody>()) EP.Destroy(physics);

				foreach(var child in editObject.Children()) {
					// Limit prefab recursion
					var prefabAssetType = PrefabUtility.GetPrefabAssetType(child);
					if(prefabAssetType == PrefabAssetType.MissingAsset) continue;
					if(prefabAssetType != PrefabAssetType.NotAPrefab && !prefabs) continue;

					ApplyTo(child);
				}
			}
		}

		static public void GameObjectSetStatic(GameObject gameObject) {
			Undo.RecordObject(gameObject, "GameObject Set Static");
			GameObjectUtility.SetStaticEditorFlags(gameObject, (StaticEditorFlags)~0);
		}

		static public void RendererSetStatic(Renderer renderer) {
			Undo.RecordObject(renderer, "Renderer Set Static");
			renderer.receiveShadows = true;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
			renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
			renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;
			renderer.allowOcclusionWhenDynamic = false;
			renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;

			var meshRenderer = renderer as MeshRenderer;
			if(meshRenderer) {
				meshRenderer.receiveGI = ReceiveGI.Lightmaps;
				meshRenderer.scaleInLightmap = 1f;
				// OPTION: Look in parents for LODGroup managing this renderer
				// and renconfigure to use probes if lower detail, or scale up
			}
		}

		static public void ColliderSetStatic(Collider collider) {
			Undo.RecordObject(collider, "Collider Set Static");
			var meshCollider = collider as MeshCollider;
			if(meshCollider) {
				meshCollider.cookingOptions = (MeshColliderCookingOptions)~0;
			}
		}

		static public void LightSetStatic(Light light) {
			Undo.RecordObject(light, "Light Set Static");
			light.lightmapBakeType = LightmapBakeType.Baked;
			light.shadows = LightShadows.Soft;
		}
	}
}
