using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Reification {
	public static class Rhino {
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

			// Rhino -> Unity mesh vertices in local coordinates
			sharedMesh.vertices = new Vector3[] {
				placeholder.InverseTransformPoint(origin),
				placeholder.InverseTransformPoint(new Vector3(-basisX.x, basisX.y, -basisX.z) + origin),
				placeholder.InverseTransformPoint(new Vector3(-basisZ.x, basisZ.y, -basisZ.z) + origin),
				placeholder.InverseTransformPoint(new Vector3(-basisY.x, basisY.y, -basisY.z) + origin)
			};
		}

		[InitializeOnLoad]
		public class Rhino5Importer: RePort.Importer {
			public string exporter { get => "3dm_5"; }

			public void ImportMeshHierarchy(Transform child, RePort.Element element) {
				// Rotate each layer to be consistent with Rhino6 import
				child.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f) * child.localRotation;

				if(element == RePort.Element.places) {
					var meshFilterList = child.GetComponentsInChildren<MeshFilter>();
					foreach(var meshFilter in meshFilterList) ImportPlaceholder(meshFilter);
				}
			}

			static Rhino5Importer() {
				RePort.RegisterImporter(new Rhino5Importer());
			}
		}

		[InitializeOnLoad]
		public class Rhino6Importer: RePort.Importer {
			public string exporter { get => "3dm_6"; }

			public void ImportMeshHierarchy(Transform child, RePort.Element element) {
				if(element == RePort.Element.places) {
					var meshFilterList = child.GetComponentsInChildren<MeshFilter>();
					foreach(var meshFilter in meshFilterList) ImportPlaceholder(meshFilter);
				}
			}

			static Rhino6Importer() {
				RePort.RegisterImporter(new Rhino6Importer());
			}
		}
	}
}
