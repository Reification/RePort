// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Reification {
  public class AutoLightDirect {
		const string menuItemName = "Reification/Auto Light Direct";
		const int menuItemPriority = 32;

		[MenuItem(menuItemName, validate = true, priority = menuItemPriority)]
		private static bool Validate() {
			return Selection.gameObjects.Length > 0;
		}

		[MenuItem(menuItemName, priority = menuItemPriority)]
		private static void Execute() {
			Undo.IncrementCurrentGroup();
			Undo.SetCurrentGroupName("Auto Light Charts");

			var selectionList = Selection.gameObjects;
			foreach(var selection in selectionList) ApplyTo(selection);
		}

		/// <summary>
		/// Configure all child MeshRenderers of GameObject for direct lighting
		/// </summary>
		public static void ApplyTo(GameObject gameObject) {
			using(var editScope = new EP.EditGameObject(gameObject)) {
				var editObject = editScope.editObject;
				var meshRendererList = new List<MeshRenderer>(editObject.GetComponentsInChildren<MeshRenderer>());
				foreach(var meshRenderer in meshRendererList) ConvertToLightDirect(meshRenderer);
			}
		}

		/// <summary>
		/// Configure MeshRenderer receive only direct lighting and contribute indirect lighting
		/// </summary>
		/// <param name="meshRenderer"></param>
		/// <param name="keepUnused"></param>
		public static void ConvertToLightDirect(MeshRenderer meshRenderer, bool keepUnused = false) {
			var staticFlags = GameObjectUtility.GetStaticEditorFlags(meshRenderer.gameObject);
			staticFlags |= StaticEditorFlags.ContributeGI;
			GameObjectUtility.SetStaticEditorFlags(meshRenderer.gameObject, staticFlags);
			meshRenderer.receiveGI = ReceiveGI.LightProbes;
			meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

			meshRenderer.receiveShadows = true;
			meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
			// IDEA: Use lowest level of detail as shadow proxy

			meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

			var sharedMesh = AutoLightCharts.SharedMesh(meshRenderer);
			if(!sharedMesh) return;
			if(!keepUnused && sharedMesh.uv2.Length != 0) sharedMesh.uv2 = new Vector2[0];
		}
	}
}
