// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Reification {
	public class AutoLightSources {
		const string menuItemName = "Reification/Auto Light Sources";
		const int menuItemPriority = 31;

		[MenuItem(menuItemName, validate = true, priority = menuItemPriority)]
		private static bool Validate() {
			return Selection.gameObjects.Length > 0;
		}

		[MenuItem(menuItemName, priority = menuItemPriority)]
		private static void Execute() {
			Undo.IncrementCurrentGroup();
			Undo.SetCurrentGroupName("Auto Light Probes");

			var selectionList = Selection.gameObjects;
			foreach(var selection in selectionList) ApplyTo(selection);
		}

		static public void ApplyTo(GameObject gameObject) {
			// Find all lights
			var lightList = gameObject.GetComponentsInChildren<Light>();
			foreach(var light in lightList) CreateSource(light);
		}

		static string LightSourceName(Light light) => light.name + "_Source";

		static public void CreateSource(Light light) {
			// Check if source already exists
			var sourceName = LightSourceName(light);
			var lighSourceList = light.transform.NameFindInChildren(sourceName);
			if(lighSourceList.Length > 0) return;
			// OPTION: If sources are found, reconfigure or destroy and recreate

			// OPTION: Move light forward to prevent Z fighting when coplanar

			switch(light.type) {
			case LightType.Point:
				CreatePointSource(light);
				break;
			case LightType.Spot:
				CreateSpotSource(light);
				break;
			case LightType.Disc:
				CreateDiskSource(light);
				break;
			case LightType.Rectangle:
				CreateRectangleSource(light);
				break;
			default: // Directional light has no position source
				break;
			}
		}

		// TODO: Rescale light diameter to world units?
		// QUESTION: How will this work with rescaled prefabs?

		// OBSERVATION: Area lights are not rescaled, including their direction
		// NOTE: In contrast, physics objects rescaled by maximum axis of each transform
		// TEMP: Assume no rescaling is applied to model...

		// OPTION: Set shadowRadius according to point diameter (if invariant, multiply by primary ray distance)
		// OPTION: Set shadowNearPlane to be outside of the emissive source

		const float pointDiameter = 0.05f; // meters

		static public GameObject CreatePointSource(Light light) {
			var source = CreatePrimitiveSource(light, PrimitiveType.Sphere);
			source.transform.localScale = Vector3.zero * pointDiameter;
			return source;
		}

		static public GameObject CreateSpotSource(Light light) {
			var source = CreatePrimitiveSource(light, PrimitiveType.Sphere);
			source.transform.localScale = Vector3.zero * pointDiameter;
			return source;
		}

		const float areaThickness = 0.005f; // meters

		static public GameObject CreateDiskSource(Light light) {
			var source = CreatePrimitiveSource(light, PrimitiveType.Cylinder);
			source.transform.localScale = new Vector3(light.areaSize.x, light.areaSize.y, areaThickness);
			source.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // cylinder Y -> disk Z
			return source;
		}

		static public GameObject CreateRectangleSource(Light light) {
			var source = CreatePrimitiveSource(light, PrimitiveType.Cube);
			source.transform.localScale = new Vector3(light.areaSize.x, light.areaSize.y, areaThickness);
			return source;
		}

		// PROBLEM: Light source meshes are generally small and could use a lower level of detail
		// in most cases. Prefabs with custom meshes could address this for sphere and cylinder

		static public GameObject CreatePrimitiveSource(Light light, PrimitiveType primitiveType) {
			var source = GameObject.CreatePrimitive(primitiveType);
			source.SetActive(light.enabled);
			source.layer = light.gameObject.layer;
			// Make source constitent with search
			source.name = LightSourceName(light);
			EP.SetParent(source.transform, light.transform);
			
			// Position source around light
			source.transform.localPosition = Vector3.zero;
			source.transform.localRotation = Quaternion.identity;
			// Source scale depends on light type

			Object.DestroyImmediate(source.GetComponent<Collider>());

			if(light.gameObject.isStatic) {
				var staticFlags = (StaticEditorFlags)~0;
				staticFlags &= ~StaticEditorFlags.OccluderStatic;
				staticFlags &= ~StaticEditorFlags.ContributeGI;
				GameObjectUtility.SetStaticEditorFlags(source, staticFlags);
			} else {
				source.isStatic = false;
			}

			LightSourceMeshRenderer(light, source.GetComponent<MeshRenderer>());

			Undo.RegisterCreatedObjectUndo(source, "Create Primitive Light Source");
			return source;
		}

		static public void LightSourceMeshRenderer(Light light, MeshRenderer meshRenderer) {
			meshRenderer.receiveShadows = false;
			meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			meshRenderer.receiveGI = ReceiveGI.LightProbes;
			meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
			meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
			meshRenderer.scaleInLightmap = 0f;
			meshRenderer.allowOcclusionWhenDynamic = true;
			meshRenderer.motionVectorGenerationMode = light.gameObject.isStatic ? MotionVectorGenerationMode.Camera : MotionVectorGenerationMode.Object;

			// Prioritize renderer illumination otherwise direct emissive illumination will be absent
			// NOTE: This is distinct from Renderer.rendererPriority
			var serializedObject = new SerializedObject(meshRenderer);
			serializedObject.FindProperty("m_ImportantGI").boolValue = false; // Editor: Prioritize Illumination = off
			serializedObject.ApplyModifiedProperties();

			// TODO: Use GetSharedMaterial and SetSharedMaterial extensions to apply this to indexed material
			// NOTE: Could also create a material using Shader.Find("Standard") if material index < 0
			var material = new Material(meshRenderer.sharedMaterial);
			LightSourceMaterial(light, material);
			material.name = meshRenderer.name;
			// OPTION: Save the material as independent asset
			meshRenderer.sharedMaterial = material;
		}

		/// <summary>
		/// Configure material emission to match light emission
		/// </summary>
		static public void LightSourceMaterial(Light light, Material material) {
			// Support use by light object prefabs
			material.enableInstancing = !light.gameObject.isStatic;

			// Light already exists, so emission should not contribute
			// Emission overrides environmental visuals so there is no need to receive
			material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

			// Assume emission will override environmental visuals
			material.color = light.color;
			material.SetFloat("_Metallic", 0f);
			material.SetFloat("_Glossiness", 0f); // Smoothness
			material.SetFloat("_SpecularHighlights", 0f); // Specular Highlights
			material.SetFloat("_GlossyReflections", 0f); // Reflections

			// Match emission to light
			material.EnableKeyword("_EMISSION");
			material.SetVector("_EmissionColor", light.color * light.intensity);

			// Use albedo as emission texture if it exists
			var texture = material.GetTexture("_MainTex");
			if(texture) material.SetTexture("_EmissionMap", texture);
		}
	}
}
