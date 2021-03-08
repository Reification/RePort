﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Reification {
	/// <summary>
	/// Importers for Rhinoceros 3D
	/// </summary>
	/// <remarks>
	/// The following constituent elements may be present:
	/// - meshes : mesh objects
	/// - meshes# : parametric objects exported with detail level #
	/// - lights : light objects with parameter placeholders
	/// - places : block instance transform placeholders
	/// The following light types may be present:
	/// - SunLight : Directional Light
	/// - PointLight : Point Light
	/// - SpotLight : Spot Light
	/// - QuadLight : Area Light
	/// - LineLight : imported as ???
	/// - UnknownLight : imported as disabled PointLight
	/// </remarks>
	public static class Rhino {
		// Derive transform information from a placeholder
		public static void ImportPlaceholder(MeshFilter meshFilter) {
			var sharedMesh = meshFilter.sharedMesh;
			var vertices = sharedMesh?.vertices;
			if(vertices == null || vertices.Length != 4) {
				Debug.LogWarning($"Inconsistent placeholder mesh: {meshFilter.Path()}.{sharedMesh.name}");
				return;
			}
			var placeholder = meshFilter.transform;

			// Derive Rhino transform block basis in world coordinates
			var origin = placeholder.TransformPoint(vertices[0]);
			var basisX = placeholder.TransformPoint(vertices[1]) - origin;
			var basisY = placeholder.TransformPoint(vertices[2]) - origin;
			var basisZ = placeholder.TransformPoint(vertices[3]) - origin;

			// Convert to unity basis in world coordinates
			var unityX = new Vector3(-basisX.x, basisX.y, -basisX.z);
			var unityY = new Vector3(-basisZ.x, basisZ.y, -basisZ.z);
			var unityZ = new Vector3(-basisY.x, basisY.y, -basisY.z);

			// TODO: Use SVD to construct transform, which can include shear
			// TEMP: Assume transform is axial scaling followed by rotation only
			// NOTE: The origin and bases are simply the columns of an affine (3x4) transform matrix
			placeholder.localScale = new Vector3(unityX.magnitude, unityY.magnitude, unityZ.magnitude);
			placeholder.rotation = Quaternion.LookRotation(basisZ, basisY);
			placeholder.position = origin;

			// Remove meshes from placeholders
			EP.Destroy(meshFilter);
		}

		// Derive light configuration from a placeholder
		public static void ConfigureLight(Light light) {
			var children = light.transform.parent.Children();
			Transform placeholder = null;
			foreach(var child in children) {
				// NOTE: Use string.Contains since 
				if(!child.name.Contains("=" + light.name)) continue;
				placeholder = child;
				break;
			}
			if(!placeholder) {
				Debug.LogWarning($"Missing placeholder for {light.Path()}");
				return;
			}

			// Apply rigid transform
			light.transform.localPosition = placeholder.localPosition;
			light.transform.localRotation = placeholder.localRotation;
			light.transform.localScale = Vector3.one;

			// Configure using transform local scale parameters
			var lightType = placeholder.name.Split('=')[0];
			switch(lightType) {
			case "DirectionalLight":
				light.type = LightType.Directional;
				break;
			case "PointLight":
				light.type = LightType.Point;
				break;
			case "SpotLight":
				light.type = LightType.Spot;
				light.spotAngle = 2f * Mathf.Atan2(placeholder.localScale.z, placeholder.localScale.x) * Mathf.Rad2Deg;
				light.innerSpotAngle = 2f * Mathf.Atan2(placeholder.localScale.z, placeholder.localScale.y) * Mathf.Rad2Deg;
				break;
			case "RectangularLight":
				light.type = LightType.Rectangle;
				light.areaSize = new Vector2(4f * placeholder.localScale.y, 4f * placeholder.localScale.x);
				break;
			default:
				// NOTE: Disk Lights are not supported in Rhino
				// PROBLEM: Line Lights are not supported in Unity
				Debug.LogWarning($"Unsupported light type {lightType} -> configure as point light");
				light.type = LightType.Point;
				break;
			}

			EP.Destroy(placeholder.gameObject);
		}

		// TODO: Configure imported light
		// - Transform and shape set by placeholder
		// NOTE: AutoStatic or AutoLights will set to bake with hard or soft shadows.
		// NOTE: AutoLights will create emissive, non-contributing visible light sources.

		[InitializeOnLoad]
		public class RhinoImporter: RePort.Importer {
			static RhinoImporter() {
				var importer = new RhinoImporter();
				RePort.RegisterImporter("3dm_7", importer);
				RePort.RegisterImporter("3dm_6", importer);
			}

			public virtual void ConfigureImport(ModelImporter importer, string element) {
				switch(element) {
				case "places":
					RePort.PlacesImporter(importer);
					break;
				case "lights":
					RePort.PlacesImporter(importer);
					importer.importLights = true;
					break;
				default:
					RePort.MeshesImporter(importer);
					break;
				}
			}

			public virtual void ImportHierarchy(Transform hierarchy, string element) {
				if(element == "places" || element == "lights") {
					var meshFilterList = hierarchy.GetComponentsInChildren<MeshFilter>();
					foreach(var meshFilter in meshFilterList) ImportPlaceholder(meshFilter);
				}
			}

			public virtual void ImportMaterial(Material material, string element) { }

			public virtual void ImportModel(GameObject model, string element) {
				switch(element) {
				case "lights":
					var lightList = model.GetComponentsInChildren<Light>();
					foreach(var light in lightList) ConfigureLight(light);
					break;
				case "places":
					// Keep empty placeholder transforms
					break;
				default:
					RePort.RemoveEmpty(model);
					break;
				}
			}
		}

		[InitializeOnLoad]
		public class Rhino5Importer: RhinoImporter {
			static Rhino5Importer() {
				RePort.RegisterImporter("3dm_5", new Rhino5Importer());
			}

			public override void ImportHierarchy(Transform hierarchy, string element) {
				// Rotate each layer to be consistent with Rhino6 import
				hierarchy.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f) * hierarchy.localRotation;

				base.ImportHierarchy(hierarchy, element);
			}
		}
	}
}
