﻿// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Reification {
	public static class MeshExtensions {
		/// <summary>Count of vertices associated with object</summary>
		/// <param name="recurse">when true all child objects will be included</param>
		public static int VertexCount(this GameObject gameObject, bool recurse = false) {
			if(!gameObject) return 0;

			int count = 0;
			if(!recurse) {
				var meshFilter = gameObject.GetComponent<MeshFilter>();
				if(meshFilter) count += meshFilter.sharedMesh?.vertexCount ?? 0;
			} else {
				var meshFilterList = gameObject.GetComponentsInChildren<MeshFilter>(true);
				foreach(var meshFilter in meshFilterList) count += meshFilter.sharedMesh?.vertexCount ?? 0;
			}
			return count;
		}

		/// <summary>Computes the area of the mesh as transformed to world coordinates</summary>
		/// <param name="subMeshIndex">Index of submesh, or all submeshes if defaulted</param>
		/// <remarks>
		/// When there are more materials than submeshes the final subMesh will be rendered with multiple materials.
		/// Therefore if subMeshIndex > subMeshCount - 1 it will be clamped to subMeshCount - 1. 
		/// </remarks>
		public static float WorldArea(this MeshFilter meshFilter, int subMeshIndex = -1) {
			var meshIndex = Mathf.Min(meshFilter.sharedMesh.subMeshCount - 1, subMeshIndex);
			var triangles = subMeshIndex < 0 ? meshFilter.sharedMesh.triangles : meshFilter.sharedMesh.GetTriangles(meshIndex);
			var vertices = meshFilter.sharedMesh.vertices;

			// Compute mesh surface area
			var area = 0f;
			for(int t = 0; t < triangles.Length; t += 3) {
				var p0 = meshFilter.transform.TransformPoint(vertices[triangles[t]]);
				var p1 = meshFilter.transform.TransformPoint(vertices[triangles[t + 1]]);
				var p2 = meshFilter.transform.TransformPoint(vertices[triangles[t + 2]]);
				area += Vector3.Cross(p1 - p0, p2 - p0).magnitude / 2f;
			}
			return area;
		}

		// QUESTION: Does CombineMeshes preserve material associations?

		/// <summary>Computes the combined mesh in world coordinates</summary>
		/// <remarks>
		/// This method will ignore meshes that do not permit read access
		/// https://docs.unity3d.com/ScriptReference/Mesh.CombineMeshes.html
		/// </remarks>
		public static Mesh Combined(this GameObject gameObject) {
			MeshFilter[] meshFilterList = gameObject.GetComponentsInChildren<MeshFilter>(true);
			CombineInstance[] combineList = new CombineInstance[meshFilterList.Length];

			for(int i = 0; i < meshFilterList.Length; ++i) {
				// NOTE: This step will fail if meshes are not accessible with error:
				// Cannot combine mesh that does not allow access: Combined Mesh (root: scene) 2
				if(!meshFilterList[i].sharedMesh.isReadable) {
					Debug.LogWarning("MeshExtensions.MergedBounds " + meshFilterList[i].name + ".sharedMesh must be readable & non-static -> skipping mesh");
					continue;
				}

				combineList[i].mesh = meshFilterList[i].sharedMesh;
				combineList[i].transform = meshFilterList[i].transform.localToWorldMatrix;
			}

			var mesh = new Mesh();
			mesh.CombineMeshes(combineList);
			return mesh;
		}


		/// <summary>
		/// Assign a deep copy to this mesh
		/// </summary>
		/// <remarks>
		/// If mesh is an asset then this will modify the asset
		/// </remarks>
		public static void Copy(this Mesh mesh, Mesh copyFrom) {
			mesh.Clear();
			mesh.indexFormat = copyFrom.indexFormat;

			// Set vertices before triangles
			// https://docs.unity3d.com/ScriptReference/Mesh-triangles.html
			mesh.vertices = copyFrom.vertices;
			mesh.subMeshCount = copyFrom.subMeshCount;
			for(int subMesh = 0; subMesh < copyFrom.subMeshCount; ++subMesh) {
				mesh.SetIndices(copyFrom.GetIndices(subMesh), copyFrom.GetTopology(subMesh), subMesh, true, (int)copyFrom.GetBaseVertex(subMesh));
			}

			mesh.boneWeights = copyFrom.boneWeights;
			mesh.bindposes = copyFrom.bindposes;

			// TODO: BlendShapePose

			mesh.bounds = copyFrom.bounds;
			mesh.normals = copyFrom.normals;
			mesh.tangents = copyFrom.tangents;

			// QUESTION: Does this preserve the representation?
			mesh.colors32 = copyFrom.colors32;
			mesh.colors = copyFrom.colors;

			// Maximum of 8 channels
			// https://docs.unity3d.com/ScriptReference/Mesh.SetUVs.html
			for(int channel = 0; channel < 8; ++channel) {
				var meshUVs = new List<Vector2>();
				copyFrom.GetUVs(channel, meshUVs);
				mesh.SetUVs(channel, meshUVs);
			}
		}

		/// <summary>
		/// Invert the orientation of a mesh
		/// </summary>
		public static Mesh Inverted(this Mesh mesh) {
			// TODO: Ensure that mesh is editable
			var inverted = new Mesh();
			inverted.Copy(mesh);
			var triangles = inverted.triangles;
			for(int trianglesIndex = 0; trianglesIndex < mesh.triangles.Length; trianglesIndex += 3) {
				// TODO: Modify this based on mesh topology type
				// Triangle reversal
				var swap = triangles[trianglesIndex];
				triangles[trianglesIndex] = triangles[trianglesIndex + 2];
				triangles[trianglesIndex + 2] = swap;
			}
			inverted.triangles = triangles;

			inverted.RecalculateBounds();
			inverted.RecalculateNormals();
			inverted.RecalculateTangents();

			return inverted;
		}

		// Specified lightmap UV channel is 2
		// https://docs.unity3d.com/462/Documentation/Manual/LightmappingUV.html
		// NOTE: uv2 has index 1
		// https://docs.unity3d.com/2019.2/Documentation/ScriptReference/Mesh.html
		const int lightmapUVChannel = 1;

		public static Mesh AddBackside(this Mesh mesh) {
			// TODO: Ensure that mesh is editable

			var back = mesh.Inverted();

			// Shift original UVs to lower quadrant
			var meshLightmapUVs = new List<Vector2>();
			mesh.GetUVs(lightmapUVChannel, meshLightmapUVs);
			for(int uvIndex = 0; uvIndex < meshLightmapUVs.Count; ++uvIndex) {
				var uv = meshLightmapUVs[uvIndex];
				meshLightmapUVs[uvIndex] = uv * 0.5f;
			}
			mesh.SetUVs(lightmapUVChannel, meshLightmapUVs);

			// Shift backside UVs to upper quadrant
			var backLightmapUVs = new List<Vector2>();
			back.GetUVs(lightmapUVChannel, backLightmapUVs);
			for(int uvIndex = 0; uvIndex < backLightmapUVs.Count; ++uvIndex) {
				var uv = backLightmapUVs[uvIndex];
				backLightmapUVs[uvIndex] = (uv + Vector2.one) * 0.5f;
			}
			back.SetUVs(lightmapUVChannel, backLightmapUVs);

			CombineInstance[] combineList = new CombineInstance[2];
			combineList[0].mesh = mesh;
			combineList[1].mesh = back;
			var addBackside = new Mesh();
			addBackside.CombineMeshes(combineList, false, false, false);

			return addBackside;
		}

		// TODO: Oriented : unifies the orientation of each connected mesh
		// PROBLEM: This requires 

		// TODO: Separated : splits mesh into unconnected parts.
		// This can use the mesh filter since the individual meshes will correspond to materials

#if UNITY_EDITOR
		public enum ReadableState {
			Readable,
			Unreadable,
			UnreadableAndUploadedToGPU
		}
		/// <summary>
		/// sets mesh asset readability.
		/// </summary>
		/// <param name="mesh">extension method - this mesh</param>
		/// <param name="state">new state</param>
		/// <returns>false if state did not change. returns true if state changed.</returns>
		public static bool SetReadable(this Mesh mesh, ReadableState state) {
			bool isStateReadable = (state == ReadableState.Readable);

			if(!mesh || (isStateReadable == mesh.isReadable)) {
				return false;
			}

			if(state == ReadableState.UnreadableAndUploadedToGPU) {
				mesh.UploadMeshData(true);
			} else {
				var serializedObject = new UnityEditor.SerializedObject(mesh);
				serializedObject.FindProperty("m_IsReadable").boolValue = (state == ReadableState.Readable);
				serializedObject.ApplyModifiedProperties();
			}

			return true;
		}
#endif // UNITY_EDITOR
	}
}
