// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using UnityEngine;
using UnityEditor;

namespace Reification {
	public class AutoLightSources {
		const string menuItemName = "Reification/Auto Light Sources";
		const int menuItemPriority = 34;

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

		public static void ApplyTo(GameObject gameObject, bool prefabs = false) {
			// NOTE: Light sources can be created as prefab overrides,
			// so the prefab does NOT need to be opened for editing

			// NOTE: Multiple light components are disallowed
			var light = gameObject.GetComponent<Light>();
			if(light) {
				CreateSource(light);
				// NOTE: Create source will remove all children of light
				return;
			}

			foreach(var child in gameObject.Children()) {
				if(
					!prefabs &&
					PrefabAssetType.NotAPrefab != PrefabUtility.GetPrefabAssetType(child) &&
					child == PrefabUtility.GetNearestPrefabInstanceRoot(child)
				) continue;
				ApplyTo(child);
			}
		}
		static string ConfigureName(string name) => name + "_Source";

		public static void CreateSource(Light light) {
			// ASSUME: All children of light are sources
			foreach(var source in light.gameObject.Children()) EP.Destroy(source);

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

		// OBSERVATION: Area lights are not rescaled, including their direction
		// NOTE: In contrast, physics objects rescaled by maximum axis of each transform
		// TEMP: Assume no rescaling is applied to model...

		// QUESTION: Should light diameters be rescaled light to world units?
		// QUESTION: How will this work with rescaled prefabs?

		// OPTION: Set shadowRadius according to point diameter (if invariant, multiply by primary ray distance)
		// OPTION: Set shadowNearPlane to be outside of the emissive source

		const float pointDiameter = 0.05f; // meters

		static void CreatePointSource(Light light) {
			var source = CreatePrimitiveSource(light, PrimitiveType.Sphere);
			source.transform.localScale = Vector3.one * pointDiameter;
		}

		static void CreateSpotSource(Light light) {
			var source = CreatePrimitiveSource(light, PrimitiveType.Sphere);
			source.transform.localScale = Vector3.one * pointDiameter;
		}

		const float areaThickness = 0.005f; // meters

		static void CreateRectangleSource(Light light) {
			if(light.areaSize.x < areaThickness && light.areaSize.y < areaThickness) {
				light.type = LightType.Point;
				CreatePointSource(light);
				return;
			}
			if(light.areaSize.y < areaThickness) {
				CreateLinearXSource(light);
				light.type = LightType.Point;
				light.enabled = false;
				return;
			}
			if(light.areaSize.x < areaThickness) {
				CreateLinearYSource(light);
				light.type = LightType.Point;
				light.enabled = false;
				return;
			}

			var source = CreatePrimitiveSource(light, PrimitiveType.Cube);
			source.transform.localScale = new Vector3(light.areaSize.x, light.areaSize.y, areaThickness);
		}

		/// <summary>
		/// Make a copy of light source as area light, with equivalent illumination
		/// </summary>
		/// <remarks>
		/// Intensity is scaled relative to the area of the light
		/// </remarks>
		public static GameObject MakeAreaCopy(Light light, Vector2 areaSize) {
			var gameObject = EP.Instantiate();
			GameObjectUtility.SetStaticEditorFlags(gameObject, (StaticEditorFlags)~0);
			EP.SetParent(gameObject.transform, light.transform.parent);
			gameObject.transform.localPosition = light.transform.localPosition;
			gameObject.transform.localRotation = light.transform.localRotation;
			var areaLight = EP.AddComponent<Light>(gameObject);
			areaLight.lightmapBakeType = LightmapBakeType.Baked;
			areaLight.type = LightType.Rectangle;
			areaLight.areaSize = areaSize;
			areaLight.intensity = light.intensity / (areaSize.x * areaSize.y);
			areaLight.color = light.color;
			areaLight.range = light.range;
			return gameObject;
		}

		// TODO: This, like a grid, is a standard layout
		// IDEA: When TransformData is included, provide utilities to generate layouts, and to replicate gameobjects over them
		static GameObject[] RotateCopies(GameObject original, Quaternion rotation, int count) {
			var copyList = new GameObject[count];
			copyList[0] = original;
			for(int c = 1; c < count; ++c) {
				var copy = EP.Instantiate(original);
				EP.SetParent(copy.transform, original.transform.parent);
				copy.transform.localPosition = rotation * copyList[c - 1].transform.localPosition;
				copy.transform.localRotation = rotation * copyList[c - 1].transform.localRotation;
				copyList[c] = copy;
			}
			return copyList;
		}

		// Number of area lights used to approximate a linear light
		const int linearSources = 4;

		static void CreateLinearXSource(Light light) {
			// Create a self-illuminated cylinder
			var source = CreatePrimitiveSource(light, PrimitiveType.Cylinder);
			source.transform.localScale = new Vector3(pointDiameter, light.areaSize.x / 2f, pointDiameter);
			source.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); // cylinder Y -> linear X

			// Create planes covering all emission directions
			var side0 = MakeAreaCopy(light, new Vector2(light.areaSize.x, pointDiameter));
			EP.SetParent(side0.transform, light.transform);
			var sideList = RotateCopies(side0, Quaternion.Euler(360f / linearSources, 0f, 0f), linearSources);
			for(int s = 0; s < sideList.Length; ++s) sideList[s].name = light.name + "_Side" + s;
		}

		static void CreateLinearYSource(Light light) {
			// Create a self-illuminated cylinder
			var source = CreatePrimitiveSource(light, PrimitiveType.Cylinder);
			source.transform.localScale = new Vector3(pointDiameter, light.areaSize.y / 2f, pointDiameter);

			// Create planes covering all emission directions
			var side0 = MakeAreaCopy(light, new Vector2(pointDiameter, light.areaSize.y));
			EP.SetParent(side0.transform, light.transform);
			var sideList = RotateCopies(side0, Quaternion.Euler(360f / linearSources, 0f, 0f), linearSources);
			for(int s = 0; s < sideList.Length; ++s) sideList[s].name = light.name + "_Side" + s;
		}

		// WARNING: Disk light sources are not supported by Enlighten
		static void CreateDiskSource(Light light) {
			// NOTE: Disk radius is encoded in rectangle X
			if(light.areaSize.x <= 0f) {
				light.type = LightType.Point;
				CreatePointSource(light);
				return;
			}
			var source = CreatePrimitiveSource(light, PrimitiveType.Cylinder);
			source.transform.localScale = new Vector3(light.areaSize.x, light.areaSize.x, areaThickness);
			source.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // cylinder Y -> disk Z
		}

		// PROBLEM: Light source meshes are generally small and could use a lower level of detail
		// in most cases. Prefabs with custom meshes could address this for sphere and cylinder

		// OPTION: Provide a component to monitor the light intensity and update the emissive material
		// accordingly - both static and dynamic. This could also tag actual child light sources.

		public static GameObject CreatePrimitiveSource(Light light, PrimitiveType primitiveType) {
			var source = GameObject.CreatePrimitive(primitiveType);
			source.SetActive(light.enabled);
			source.layer = light.gameObject.layer;
			// Make source constitent with search
			source.name = ConfigureName(light.name);
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

		public static void LightSourceMeshRenderer(Light light, MeshRenderer meshRenderer) {
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

		const float bloomZero = 1f;

		/// <summary>
		/// Configure material emission to match light emission
		/// </summary>
		public static void LightSourceMaterial(Light light, Material material) {
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
			material.SetVector("_EmissionColor", light.color * (bloomZero + light.intensity));

			// Use albedo as emission texture if it exists
			var texture = material.GetTexture("_MainTex");
			if(texture) material.SetTexture("_EmissionMap", texture);
		}
	}
}
