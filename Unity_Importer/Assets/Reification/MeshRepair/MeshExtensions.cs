// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Collections;
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
		/// WARNING: This assumes a Triangles mesh topology
		/// </remarks>
		public static float WorldArea(this MeshFilter meshFilter, int subMeshIndex = -1) {
			var meshIndex = Mathf.Min(meshFilter.sharedMesh.subMeshCount - 1, subMeshIndex);
			var triangles = subMeshIndex < 0 ? meshFilter.sharedMesh.triangles : meshFilter.sharedMesh.GetTriangles(meshIndex);
			var vertices = meshFilter.sharedMesh.vertices;

			// TODO: Verify that a triangle topology is in use
			// A better approach would be to adapt to each topology

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

		// TODO: SideArea = surface area visible from one side, without overlap checking
		// NOTE: This is needed for improved LOD thresholds
		// NOTE: Fractional area calculation can also be used for SingleSide triangle identification

		// WARNING: Attempting to modify to static meshes during play yields "Invalid AABB" errors
		// In particular, adding backsides to meshes with baked lighting yields this error.

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

		// TODO: A Separated() method that splits submeshes into separate objects
		
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
				mesh.SetIndices(
					copyFrom.GetIndices(subMesh),
					copyFrom.GetTopology(subMesh),
					subMesh,
					true,
					(int)copyFrom.GetBaseVertex(subMesh)
				);
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
		/// <remarks>
		/// WARNING: This assumes a Triangles mesh topology
		/// </remarks>
		public static Mesh Inverted(this Mesh mesh) {
			var inverted = new Mesh();
			inverted.Copy(mesh);

			// TODO: Handle each topology separately when inverting

			var triangles = inverted.triangles;
			for(int trianglesIndex = 0; trianglesIndex < mesh.triangles.Length; trianglesIndex += 3) {
				// TODO: Modify this based on mesh topology type
				// Triangle reversal
				var swap = triangles[trianglesIndex];
				triangles[trianglesIndex] = triangles[trianglesIndex + 2];
				triangles[trianglesIndex + 2] = swap;
			}
			inverted.triangles = triangles;

			// FIXME: Copy the normals (negated)
			// FIXME: Copy the tangents (negated??)
			// GOAL: The same bump map should be inverted when viewed from the backside
			// The tangents and binormals are generally aligned with the normalmap texture coordinates
			// but are specifically interpolated to define the basis for the normal deviation,
			// so both should be negated for the backside (the normal and tangent are negated, including tangent w)
			// https://docs.unity3d.com/ScriptReference/Mesh-tangents.html
			// NOTE: Using the same UVs will yield the desired texture lookups
			inverted.RecalculateNormals();
			inverted.RecalculateTangents();

			// NOTE: Do not optimize so that vertex order remains consistent
			inverted.RecalculateBounds();

			return inverted;
		}

		// FIXME: Check for backside created by AddBackSide
		// NOTE: Since the backside is a submesh, look for:
		// - an even number of submeshes (that are triangle / quad topologies)
		// - with an equal number of vertices
		// - where the normals are negated
		// NOTE: Vertices must be separate because normals & tangents are different


		// Specified lightmap UV channel is 2
		// https://docs.unity3d.com/462/Documentation/Manual/LightmappingUV.html
		// NOTE: uv2 has index 1
		// https://docs.unity3d.com/2019.2/Documentation/ScriptReference/Mesh.html
		const int lightmapUVChannel = 1;

		/// <summary>
		/// Add a backside to a mesh
		/// </summary>
		/// <remarks>
		/// WARNING: This assumes that no backsides currently exist
		/// WARNING: This assumes a Triangles mesh topology
		/// </remarks>
		/// <param name="mesh"></param>
		/// <returns></returns>
		public static Mesh AddBackSide(this Mesh mesh) {
			var back = mesh.Inverted();
			// NOTE: Vertices cannot be re-used for triangles,
			// because normals and tangents are different.

			// Shift original lightmap UVs to lower quadrant
			var meshLightmapUVs = new List<Vector2>();
			mesh.GetUVs(lightmapUVChannel, meshLightmapUVs);
			for(int uvIndex = 0; uvIndex < meshLightmapUVs.Count; ++uvIndex) {
				var uv = meshLightmapUVs[uvIndex];
				meshLightmapUVs[uvIndex] = uv * 0.5f;
			}
			mesh.SetUVs(lightmapUVChannel, meshLightmapUVs);

			// QUESTION: Is it possible to simply place UVs side-by-side without rescaling?
			// Or... could the UVs be recomputed using Unwrapping.GeneratePerTriangleUV?

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

		/// <summary>
		/// Create a mesh of only the triangles that are in the same direction as the side vector
		/// </summary>
		/// <remarks>
		/// WARNING: This assumes a Triangles mesh topology
		/// WARNING: Before applying lightmaps to this mesh apply Unwrapping.GenerateSecondaryUVSet().
		/// </remarks>
		/// <param name="mesh">Original mesh</param>
		/// <param name="localSide">Normal vector in mesh (local) coordinates of mesh side</param>
		/// <returns></returns>
		public static Mesh GetSingleSide(this Mesh mesh, Vector3 localSide) {
			// Create a mesh of only the normal side triangles
			var sideMesh = new Mesh();
			sideMesh.indexFormat = mesh.indexFormat;
			{
				var vertices = mesh.vertices;
				var triangles = mesh.triangles;

				// Identify vertices to keep
				var verticesKeep = new BitArray(vertices.Length, false);
				var trianglesKeep = new BitArray(triangles.Length / 3, false);
				var sideTrianglesCount = 0;
				for(int trianglesIndex = 0; trianglesIndex * 3 < triangles.Length; ++trianglesIndex) {
					var verticesIndices = new int[3];
					var triangleVertices = new Vector3[3];
					var trianglesCornerIndex = trianglesIndex * 3;
					for(var offset = 0; offset < 3; ++offset) {
						verticesIndices[offset] = triangles[trianglesCornerIndex + offset];
						triangleVertices[offset] = vertices[verticesIndices[offset]];
					}

					// FIXME: This assumes a uniform triangle topology
					// Ideally, each submesh topology would be handled separately
					// NOTE: Only Triangles and Quads have orientations

					var triangleNormal = Vector3.Cross(
						triangleVertices[1] - triangleVertices[0],
						triangleVertices[2] - triangleVertices[0]
					);
					if(Vector3.Dot(triangleNormal, localSide) <= 0f) continue;

					for(var offset = 0; offset < 3; ++offset) {
						verticesKeep[verticesIndices[offset]] = true;
					}
					trianglesKeep[trianglesIndex] = true;
					++sideTrianglesCount;
				}

				// FIXME: Remapping to a subset of vertices can be separated out
				// NOTE: The implementation can assume that every surface primitive is complete in the set
				// this allows any triangles array to be remapped with element discards (slower, but general)
				// NOTE: Alternatively, begin with the triangles array keep bitarray, then derive vertices.
				// This is needed for the boundary extraction.

				// Create a maps between vertices to normalVertices
				var toSideIndex = new Dictionary<int, int>();
				var fromSideIndex = new Dictionary<int, int>();
				for(var verticesIndex = 0; verticesIndex < vertices.Length; ++verticesIndex) {
					if(!verticesKeep[verticesIndex]) continue;

					var sideVerticesIndex = toSideIndex.Count;
					toSideIndex[verticesIndex] = sideVerticesIndex;
					fromSideIndex[sideVerticesIndex] = verticesIndex;
				}

				// Create the single side vertices, normals and tangents arrays
				{
					var normals = mesh.normals;
					var tangents = mesh.tangents;
					var sideVertices = new Vector3[toSideIndex.Count];
					var sideNormals = new Vector3[toSideIndex.Count];
					var sideTangents = new Vector4[toSideIndex.Count];
					for(var sideVerticesIndex = 0; sideVerticesIndex < sideVertices.Length; ++sideVerticesIndex) {
						var verticesIndex = fromSideIndex[sideVerticesIndex];
						sideVertices[sideVerticesIndex] = vertices[verticesIndex];
						sideNormals[sideVerticesIndex] = normals[verticesIndex];
						sideTangents[sideVerticesIndex] = tangents[verticesIndex];
					}
					sideMesh.vertices = sideVertices;
					sideMesh.normals = sideNormals;
					sideMesh.tangents = sideTangents;
				}

				// Copy the UVs so that texture lookup is preserved
				// Maximum of 8 channels
				// https://docs.unity3d.com/ScriptReference/Mesh.SetUVs.html
				for(int channel = 0; channel < 8; ++channel) {
					var meshUVs = new List<Vector2>();
					mesh.GetUVs(channel, meshUVs);
					var sideMeshUVs = new Vector2[sideMesh.vertexCount];
					for(var uvIndex = 0; uvIndex < meshUVs.Count; ++uvIndex) {
						if(!verticesKeep[uvIndex]) continue;
						sideMeshUVs[toSideIndex[uvIndex]] = meshUVs[uvIndex];
					}
					sideMesh.SetUVs(channel, sideMeshUVs);
				}

				// TODO: Instead of just copying triangles, create the submesh index arrays
				// NOTE: Each submesh index array would need to be handled separately

				// Create the single side triangle array
				{
					var sideTriangles = new int[sideTrianglesCount * 3];
					var sideTrianglesIndex = 0;
					for(var trianglesIndex = 0; trianglesIndex * 3 < triangles.Length; ++trianglesIndex) {
						if(!trianglesKeep[trianglesIndex]) continue;

						var trianglesCornerIndex = trianglesIndex * 3;
						var sideTrianglesCornerIndex = sideTrianglesIndex * 3;
						for(var offset = 0; offset < 3; ++offset) {
							sideTriangles[sideTrianglesCornerIndex + offset] = toSideIndex[triangles[trianglesCornerIndex + offset]];
						}
						++sideTrianglesIndex;
					}
					sideMesh.triangles = sideTriangles;
				}
			}

			sideMesh.Optimize();
			sideMesh.RecalculateBounds();
			return sideMesh;
		}

		/// <summary>
		/// Retain or discard a raycast
		/// </summary>
		/// <param name="hitList">all hits along the raycast, sorted by distance</param>
		/// <param name="hitIndex">selected hit index</param>
		/// <returns>true when hit should be retained</returns>
		public delegate bool RaycastValidate(Vector3 origin, Vector3 direction, float maxDistance, List<RaycastHit> hitList, int hitPick);

		/// <summary>
		/// Create a mesh by resampling with a regular spacing using physics raycasting in scene
		/// </summary>
		/// <remarks>
		/// The resampling occurs in the context of a scene, so it can be blocked by other active objects.
		/// A mesh collider is required, and the resampling will be subject to its convexity, layer, and trigger configuration.
		/// The resampling ignores the original mesh topology, so details with projected extents less than the sampling may be covered.
		/// The resampling partititions each quad on an edge from +stepX to +stepY, so the angle from stepX to stepY should be 60deg.
		/// NOTE: The resampled mesh will be in the same coordinates as the original mesh.
		/// NOTE: The raycasting will create a new UV map based on the hit locations in the original UV space.
		/// NOTE: The resulting mesh triangles have constant projected area, but surface inclines may cause the actual area to be irregular.
		/// </remarks>
		/// <param name="meshCollider">Collider to be sampled by raycasting</param>
		/// <param name="stepX">Sample spacing in first direction (alignment rounds inward)</param>
		/// <param name="stepY">Sample spacing in second direction (alignment rounds inward)</param>
		/// <param name="castZ">Negative of raycast direction, with magnitude added to bounds</param>
		/// <param name="layerMask">Raycast layerMask argument</param>
		/// <param name="queryTriggerInteraction">Raycast queryTriggerInteraction argument</param>
		/// <returns></returns>
		public static Mesh GetResample(
			this MeshCollider meshCollider, Vector3 stepX, Vector3 stepY, Vector3 castZ, RaycastValidate raycastValidate = null,
			int layerMask = Physics.DefaultRaycastLayers, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal
			) {
			var sharedMesh = meshCollider.sharedMesh;
			if(!sharedMesh) return null;

			// Define conversion from local coordinates to basis coordinates
			// NOTE: basis * basisCoord = worldCoord so inverse must be applied
			var basis = Matrix4x4.identity;
			basis.SetColumn(0, new Vector4(stepX.x, stepX.y, stepX.z, 0));
			basis.SetColumn(1, new Vector4(stepY.x, stepY.y, stepY.z, 0));
			basis.SetColumn(2, new Vector4(castZ.x, castZ.y, castZ.z, 0));
			var localToBasis = basis.inverse * meshCollider.transform.localToWorldMatrix;

			// Compute mesh bounds in basis coordintes
			Vector3 basisMin;
			Vector3 basisMax;
			{
				var vertices = sharedMesh.vertices;
				basisMin = localToBasis.MultiplyPoint3x4(vertices[0]);
				basisMax = basisMin;
				for(var v = 1; v < vertices.Length; ++v) {
					var basisPoint = localToBasis.MultiplyPoint3x4(vertices[v]);
					for(var i = 0; i < 3; ++i) {
						if(basisMin[i] > basisPoint[i]) basisMin[i] = basisPoint[i];
						if(basisMax[i] < basisPoint[i]) basisMax[i] = basisPoint[i];
					}
				}
			}

			// Compute resample counts and origin (rounding down)
			var basisMid = (basisMax + basisMin) / 2f;
			var worldMid = stepX * basisMid.x + stepY * basisMid.y + castZ * basisMid.z;
			var basisBox = basisMax - basisMin;
			var countX = Mathf.FloorToInt(basisBox.x);
			var countY = Mathf.FloorToInt(basisBox.y);
			var offset = (basisBox.z + 2f) * castZ;
			var corner = worldMid - (stepX * countX + stepY * countY + offset) / 2f;
			var direction = -offset;
			var maxDistance = offset.magnitude;

			var resample = new Mesh();
			if(countX * countY < System.UInt16.MaxValue) resample.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
			else resample.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
			// TODO: Catch higher index counts and do something...

			// Resample the mesh
			var worldToLocal = meshCollider.transform.worldToLocalMatrix;
			var castToKeep = new int[countX * countY];
			var keepPoints = new List<Vector3>();
			var keepCoords = new List<Vector2>();
			for(var x = 0; x < countX; ++x) {
				for(var y = 0; y < countY; ++y) {
					// Raycast to nearest hit on meshCollider
					var origin = corner + stepX * x + stepY * y + offset;
					var hitList = new List<RaycastHit>(Physics.RaycastAll(origin, -offset, maxDistance));
					hitList.Sort(
						(less, more) =>
						less.distance < more.distance ? -1:
						less.distance > more.distance ? 1:
						0
					);
					var meshHit = new RaycastHit();
					var hitPick = -1;
					for(int index = 0; index < hitList.Count; ++index) {
						var hitInfo = hitList[index];
						if(hitInfo.collider != meshCollider) continue;
						if(hitPick >= 0 && hitInfo.distance > meshHit.distance) continue;
						meshHit = hitInfo;
						hitPick = index;
					}

					// Validate raycast hit
					bool keepHit = hitPick >= 0;
					if(keepHit && raycastValidate != null) keepHit = raycastValidate(origin, direction, maxDistance, hitList, hitPick);
					if(!keepHit) {
						// Drop the cast vertex
						//Debug.DrawLine(origin, origin - offset, Color.red, 10f);
						castToKeep[x * countY + y] = -1;
						continue;
					}

					// Keep the cast vertex
					//Debug.DrawLine(origin, meshHit.point, Color.green, 10f);
					castToKeep[x * countY + y] = keepPoints.Count;
					keepPoints.Add(worldToLocal.MultiplyPoint3x4(meshHit.point));
					keepCoords.Add(meshHit.textureCoord);
				}
			}

			var castFaces = new List<int>();
			for(var x = 0; x < countX - 1; ++x) {
				for(var y = 0; y < countY - 1; ++y) {
					// Each casting vertex in the grid (with two edges removed) corresponds 2 triangles from 4 grid coordinates:
					// (1) V+X [2] -> V [0] -> V+Y [1]
					// (2) V+Y [1] -> V+X+Y [3] -> V+X [2]
					// Index the vertices as 2*x + y
					var quadIndices = new int[4];
					quadIndices[0] = castToKeep[x * countY + y];
					quadIndices[1] = castToKeep[x * countY + (y + 1)];
					quadIndices[2] = castToKeep[(x + 1) * countY + y];
					quadIndices[3] = castToKeep[(x + 1) * countY + (y + 1)];
					if(quadIndices[2] >= 0 && quadIndices[0] >= 0 && quadIndices[1] >= 0) {
						castFaces.Add(quadIndices[2]);
						castFaces.Add(quadIndices[0]);
						castFaces.Add(quadIndices[1]);
					}
					if(quadIndices[1] >= 0 && quadIndices[3] >= 0 && quadIndices[2] >= 0) {
						castFaces.Add(quadIndices[1]);
						castFaces.Add(quadIndices[3]);
						castFaces.Add(quadIndices[2]);
					}
				}
			}

			// IDEA: Concave V segments of the boundary could be tested for closure using an elongated equal area triangle
			// IDEA: Even straight segments might be extended in this way.
			// NOTE: longer concave segments and straight segments could have ambiguous closure.

			resample.vertices = keepPoints.ToArray();
			resample.triangles = castFaces.ToArray();
			resample.uv = keepCoords.ToArray();
			resample.Optimize();
			resample.RecalculateBounds();
			resample.RecalculateNormals();
			resample.RecalculateTangents();
			return resample;
		}

		// TODO: Oriented : unifies the orientation of each connected mesh

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
