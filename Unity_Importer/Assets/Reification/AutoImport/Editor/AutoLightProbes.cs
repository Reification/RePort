// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Reification {
	/// <summary>
	/// Adds light probes to manage illumination of levels of object detail
	/// </summary>
	/// <remarks>
	/// General information:
	/// https://docs.unity3d.com/Manual/LightProbes.html
	/// Application to lower levels of detail:
	/// https://docs.unity3d.com/2019.3/Documentation/Manual/LODForBakedGI.html
	/// Interaction with dynamic lighting:
	/// https://docs.unity3d.com/2020.1/Documentation/Manual/class-LightProbeGroup.html
	/// </remarks>
	public class AutoLightProbes {
		const string menuItemName = "Reification/Auto Light Probes";
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

		public static void ApplyTo(GameObject gameObject) {
			var probeGroupList = gameObject.GetComponentsInChildren<LightProbeGroup>();
			if(probeGroupList.Length > 1) {
				Debug.LogWarning($"AutoLightProbes {gameObject.Path()} contains {probeGroupList.Length} LightProbeGroup components -> select one group to reconfigure");
				return;
			}

			LightProbeGroup probeGroup = null;
			if(probeGroupList.Length == 1) {
				probeGroup = probeGroupList[0];
				Debug.Log($"AutoLightProbes is reconfiguring {probeGroup.Path()}.LightProbeGroup");
			} else {
				// Create new LightProbeGroup
				var probeGroupObject = EP.Instantiate();
				probeGroupObject.isStatic = true;
				probeGroupObject.name = gameObject.name + "_Probes";
				probeGroup = EP.AddComponent<LightProbeGroup>(probeGroupObject);
				EP.SetParent(probeGroupObject.transform, gameObject.transform);
				// Light probe positions are derived relative to gameObject
				// and are evaluated relative to LightProbeGroup
				// https://docs.unity3d.com/ScriptReference/LightProbeGroup-probePositions.html
				probeGroupObject.transform.localPosition = Vector3.zero;
				probeGroupObject.transform.localRotation = Quaternion.identity;
				probeGroupObject.transform.localScale = Vector3.one;
			}
			// TODO: Enable configuration to use other placement strategies
			probeGroup.probePositions = LightProbeGrid(gameObject, probeSpaces);
		}

		public static RaycastHit[] RaycastLine(Vector3 origin, Vector3 ending, int layerMask = Physics.DefaultRaycastLayers, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.Ignore) {
			// Raycast in both directions to identify enter and exit points
			var direction = ending - origin;
			var distance = direction.magnitude;
			var forwardHitList = Physics.RaycastAll(origin, direction, distance, layerMask, queryTriggerInteraction);
			var reverseHitList = Physics.RaycastAll(ending, -direction, distance, layerMask, queryTriggerInteraction);
			//Debug.Log("CAST: origin = " + origin.ToString("F7") + ", direction = " + direction.ToString() + ": forward Count = " + forwardHitList.Length + ", reverse Count = " + reverseHitList.Length);

			// Merge and sort by distance from origin
			for(int i = 0; i < reverseHitList.Length; ++i) reverseHitList[i].distance = distance - reverseHitList[i].distance;
			var hitList = new List<RaycastHit>(forwardHitList);
			hitList.AddRange(reverseHitList);
			hitList.Sort(
				(less, more) =>
				less.distance < more.distance ? -1 :
				less.distance > more.distance ? 1 :
				0
			);

			return hitList.ToArray();
		}

		enum ProbeState {
			Volume, // No adjacent surfaces
			Surface, // One or more adjacent surfaces
			Interior // Inside of surfaces
		};

		// Cast to 6 adjacent probes along world coordinates
		static ProbeState GetProbeState(Vector3 origin, GameObject root = null) {
			var endingList = new Vector3[] {
				origin + Vector3.right * probeSpaces[0],
				origin - Vector3.right * probeSpaces[0],
				origin + Vector3.up * probeSpaces[1],
				origin - Vector3.up * probeSpaces[1],
				origin + Vector3.forward * probeSpaces[2],
				origin - Vector3.forward * probeSpaces[2],
			};

			// FIXME: This runs twice on each segment - once for each side's probe
			// FIXME: If the interior of a mesh is larger than the probe spacing
			// the probe will be identified as a volume, or even a surface if there
			// is an object that overlaps the interior.

			var probeState = ProbeState.Volume;
			foreach(var ending in endingList) {
				var hitList = RaycastLine(origin, ending);

				// Index of first hit that is a child of selected object root
				int hitRoot = 0;
				for(; hitRoot < hitList.Length && root && !hitList[hitRoot].collider.transform.IsChildOf(root.transform); ++hitRoot) ;
				if(hitRoot >= hitList.Length) continue;

				// NOTE: For interior initial hits, normal == Vector3.zero
				probeState = Vector3.Dot(hitList[hitRoot].normal, ending - origin) < 0f ? ProbeState.Surface : ProbeState.Interior;
				if(probeState == ProbeState.Interior) break;
			}
			return probeState;
		}

		public static Vector3 probeSpaces = new Vector3(0.5f, 1f, 0.5f); // Modify as needed

		static Vector3 ProbesOrigin(Bounds bounds) {
			var origin = Vector3.zero;
			for(int i = 0; i < 3; ++i) {
				var count = Mathf.CeilToInt(2f * bounds.extents[i] / probeSpaces[i]);
				var envelope = count * probeSpaces[i];
				origin[i] = bounds.center[i] - envelope / 2f;
			}
			return origin;
		}

		// TODO: Configure proxy volumes for objects that exceed probe lighting and exceed probe spacing size
		// This is important for curved building surfaces that receive varying indirect light.

		// IDEA: In the absence of visible dynamic object, probes only provides illumination to lower LoD objects.
		// Consequently, if a probe does not provide lighting information for any lower levels of detail it could be removed.
		// This can be tested with a volume overlap.

		// IDEA: When light probes all touch one large object, decimate probes acording to object scale in lightmap.
		// TODO: This requires downscaling large object lightmaps (or even partitioning the objects)
		// In particular, terrain will contribute to lightmap, but will have a scale of 0,
		// so it will not receive lightmapping itself.

		/// <summary>
		/// Generate a grid of probe points adjacent to surfaces
		/// </summary>
		/// <remarks>
		/// The light probe grid is derived relative to world coordinates.
		/// </remarks>
		/// <param name="gameObject">Object to be enveloped in probes</param>
		/// <param name="probeSpaces">Spacing of probe grid</param>
		public static Vector3[] LightProbeGrid(GameObject gameObject, Vector3 probeSpaces) {
			// Get gameObject bounds
			// TODO: Move this to bounds extension.
			var emptyBounds = true;
			var worldBounds = new Bounds();
			var objectRenderers = gameObject.GetComponentsInChildren<Renderer>();
			foreach(var renderer in objectRenderers) {
				if(emptyBounds) {
					worldBounds = renderer.bounds;
					emptyBounds = false;
				} else {
					worldBounds.Encapsulate(renderer.bounds);
				}
			}

			var worldOrigin = ProbesOrigin(worldBounds);
			var probePointList = new List<Vector3>();
			var point = Vector3.zero;
			for(point[0] = worldOrigin[0]; point[0] < worldBounds.max[0] + probeSpaces[0]; point[0] += probeSpaces[0])
				for(point[1] = worldOrigin[1]; point[1] < worldBounds.max[1] + probeSpaces[1]; point[1] += probeSpaces[1])
					for(point[2] = worldOrigin[2]; point[2] < worldBounds.max[2] + probeSpaces[2]; point[2] += probeSpaces[2]) {
						var probeState = GetProbeState(point, gameObject);
						if(probeState != ProbeState.Surface) continue;
						probePointList.Add(gameObject.transform.InverseTransformPoint(point));
					}
			return probePointList.ToArray();
		}
	}
}
