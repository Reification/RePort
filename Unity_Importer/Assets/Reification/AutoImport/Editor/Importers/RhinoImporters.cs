// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

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
	/// - places : block instance transform placeholders
	/// - lights : light objects with parameter placeholders
	/// The following light types may be present:
	/// - DirectionalLight : Directional Light
	/// - PointLight : Point Light
	/// - SpotLight : Spot Light
	/// - RectangularLight : Rectangle (Area) Light
	/// - LinearLight : imported as Point Light
	/// - UnknownLight : imported as Point Light
	/// </remarks>
	public static class Rhino {
		// Derive transform information from a placeholder
		public static void ImportPlaceholder(MeshFilter meshFilter, bool rhinoBasis) {
			var sharedMesh = meshFilter.sharedMesh;
			var vertices = sharedMesh?.vertices;
			if(vertices == null || vertices.Length != 4) {
				Debug.LogWarning($"Inconsistent placeholder mesh: {meshFilter.Path()}.{sharedMesh.name}");
				return;
			}
			var placeholder = meshFilter.transform;
			// OBSERVATION: At this stage of the import the layer parent transform is present
			// but on completion the layer may be absent, although the transform will persist.
			// NOTE: This also applies to the default import path.

			// Derive Rhino transform block basis in world coordinates
			var origin = placeholder.TransformPoint(vertices[0]);
			var basisX = placeholder.TransformPoint(vertices[1]) - origin;
			var basisY = placeholder.TransformPoint(vertices[2]) - origin;
			var basisZ = placeholder.TransformPoint(vertices[3]) - origin;

			if(rhinoBasis) {
				// Mesh basis describes transformation of Rhino basis
				var unityX = new Vector3(-basisX.x, basisX.y, -basisX.z);
				var unityY = new Vector3(-basisZ.x, basisZ.y, -basisZ.z);
				var unityZ = new Vector3(-basisY.x, basisY.y, -basisY.z);
				basisX = unityX;
				basisY = unityY;
				basisZ = unityZ;
			}

			// TODO: Use SVD to construct transform, which can include shear
			// TEMP: Assume transform is axial scaling followed by rotation only
			// NOTE: The origin and bases are simply the columns of an affine (3x4) transform matrix
			placeholder.localScale = new Vector3(basisX.magnitude, basisY.magnitude, basisZ.magnitude);
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
			// NOTE: Disk Lights are not exported in Rhino
			// NOTE: Volume light type are not imported to Unity, and is not exported by Rhino
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
				light.spotAngle = Mathf.Atan2(placeholder.localScale.z, placeholder.localScale.x) * Mathf.Rad2Deg * 2f;
				light.innerSpotAngle = Mathf.Atan2(placeholder.localScale.z, placeholder.localScale.y) * Mathf.Rad2Deg * 2f;
				break;
			case "RectangularLight":
				light.type = LightType.Rectangle;
				light.areaSize = new Vector2(placeholder.localScale.x * 2f, placeholder.localScale.y * 2f);
				break;
			case "LinearLight":
				// NOTE: Linear lights are not supported in Unity
				// A subsequent conversion to a collection of finite-size rectangular lights will be required
				light.type = LightType.Rectangle;
				light.areaSize = new Vector2(placeholder.localScale.x * 2f, 0f);
				break;
			default:
				Debug.LogWarning($"Unsupported light type {lightType} -> configure as point light");
				light.type = LightType.Point;
				light.enabled = false;
				break;
			}

			EP.Destroy(placeholder.gameObject);
		}

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
				var blockBasis = element == "places";
				var lightBasis = element == "lights";
				if(blockBasis || lightBasis) {
					var meshFilterList = hierarchy.GetComponentsInChildren<MeshFilter>();
					foreach(var meshFilter in meshFilterList) ImportPlaceholder(meshFilter, blockBasis);
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
