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
	/// Probe placement will need to be regenerated only if constituent objects are moved.
	/// 
	/// General information:
	/// https://docs.unity3d.com/Manual/LightProbes.html
	/// Application to lower levels of detail:
	/// https://docs.unity3d.com/2019.3/Documentation/Manual/LODForBakedGI.html
	/// Interaction with dynamic lighting:
	/// https://docs.unity3d.com/2020.1/Documentation/Manual/class-LightProbeGroup.html
	/// </remarks>
	public class AutoLightProbes {
		const string menuItemName = "Reification/Auto Light Probes";
		const int menuItemPriority = 33;

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

		/// <summary>
		/// Configure light probe use for all child renderers according to level of detail
		/// </summary>
		/// <remarks>
		/// This must be called after AutoLightmaps.Apply()
		/// </remarks>
		public static void ApplyTo(GameObject gameObject) {
			// NOTE: Proxy volumes can be created and configured as prefab overrides,
			// so the prefab does NOT need to be opened for editing
			var probeSpaces = ProbeSpaces();

			// NOTE: Light Probe Group and Proxy Volumes can be created and configured as prefab overrides,
			// so the prefab does NOT need to be opened for editing
			CreateLightProbes(gameObject, probeSpaces);
			var lodGroupList = gameObject.GetComponentsInChildren<LODGroup>();
			foreach(var lodGroup in lodGroupList) ConfigureLODGroup(lodGroup, probeSpaces);
		}

		/// <summary>
		/// Probe spacing matching baked indirect lightmap resolution
		/// </summary>
		public static Vector3 ProbeSpaces() {
			var lightmapSettings = new AutoLightmaps.LightmapSettings();
			return Vector3.one * lightmapSettings.resolutionIndirect;
		}

		public static LightProbeGroup CreateLightProbes(GameObject gameObject, Vector3 probeSpaces) {
			// Light probe positions are evaluated relative to LightProbeGroup
			// https://docs.unity3d.com/ScriptReference/LightProbeGroup-probePositions.html
			var group = gameObject.GetComponent<LightProbeGroup>();
			if(!group) group = EP.AddComponent<LightProbeGroup>(gameObject);

			// IDEA: Find child LightProbeGroups and exclude probes in those volumes
			// This would enable adjusting the probe density in different areas.

			// TODO: Enable configuration to use other placement strategies
			group.probePositions = LightProbeGrid(gameObject, probeSpaces);
			return group;
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
		static ProbeState GetProbeState(Vector3 origin, Vector3 probeSpaces, GameObject root = null) {
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
				for(; hitRoot < hitList.Length && root && !hitList[hitRoot].collider.transform.IsChildOf(root.transform); ++hitRoot);
				if(hitRoot >= hitList.Length) continue;

				// NOTE: For interior initial hits, normal == Vector3.zero
				probeState = Vector3.Dot(hitList[hitRoot].normal, ending - origin) < 0f ? ProbeState.Surface : ProbeState.Interior;
				if(probeState == ProbeState.Interior) break;
			}
			return probeState;
		}

		static void ProbesBounds(Bounds bounds, out Vector3 origin, out Vector3Int counts, Vector3 probeSpaces) {
			origin = Vector3.zero;
			counts = Vector3Int.zero;
			for(int i = 0; i < 3; ++i) {
				// Minimum and maximum surfaces must permit offset probe placement, so add 2 for the case of an exact fit
				counts[i] = Mathf.CeilToInt(bounds.size[i] / probeSpaces[i]) + 2;
				var envelope = counts[i] * probeSpaces[i];
				origin[i] = bounds.center[i] - envelope / 2f;
				counts[i] += 1;
			}
		}

		// TODO: Move this to bounds extension.
		/// <summary>
		/// Get world bounds of all child renderers
		/// </summary>
		public static Bounds RendererWorldBounds(GameObject gameObject) {
			var bounds = new Bounds();
			var emptyBounds = true;
			var objectRenderers = gameObject.GetComponentsInChildren<Renderer>();
			foreach(var renderer in objectRenderers) {
				if(emptyBounds) {
					bounds = renderer.bounds;
					emptyBounds = false;
				} else {
					bounds.Encapsulate(renderer.bounds);
				}
			}
			return bounds;
		}

		// IDEA: In the absence of a visible dynamic object, probes only provide illumination to lower LoD objects.
		// Consequently, if a probe does not provide lighting information for any lower levels of detail it could be removed.
		// This can be tested with a volume overlap.

		// IDEA: Scale probe density based on proximate object scale in lightmap.
		// NOTE: Large objects are automatically scaled to fit in atlas, but the scale is not disclosed in editor.
		// NOTE: Large objects are converted to contributing with probes off.
		// IDEA: For small detail objects, the probe spacing could be up-sampled.

		/// <summary>
		/// Generate a grid of probe points adjacent to surfaces
		/// </summary>
		/// <remarks>
		/// The light probe grid is derived relative to world coordinates.
		/// </remarks>
		/// <param name="gameObject">Object to be enveloped in probes</param>
		/// <param name="probeSpaces">Spacing of probe grid</param>
		public static Vector3[] LightProbeGrid(GameObject gameObject, Vector3 probeSpaces) {
			var worldBounds = RendererWorldBounds(gameObject);
			ProbesBounds(worldBounds, out var worldOrigin, out var counts, probeSpaces);
			var probePointList = new List<Vector3>();
			for(var x = 0; x < counts[0]; ++x) {
				for(var y = 0; y < counts[1]; ++y) {
					for(var z = 0; z < counts[2]; ++z) {
						var point = worldOrigin + new Vector3(x * probeSpaces[0], y * probeSpaces[1], z * probeSpaces[2]);
						var probeState = GetProbeState(point, probeSpaces, gameObject);
						if(probeState != ProbeState.Surface) continue;
						probePointList.Add(gameObject.transform.InverseTransformPoint(point));
					}
				}
			}
			return probePointList.ToArray();
		}
		
		// NOTE: Corner placement of proxies is used, so 1 + Floor is used to add fencepost
		static int ProxyResolution(float spaceRatio) => Mathf.ClosestPowerOfTwo(1 + Mathf.FloorToInt(Mathf.Clamp(spaceRatio, 2f, 32f)));

		/// <summary>
		/// Configure the light probe proxy volume for lower levels of detail in group
		/// </summary>
		public static void ConfigureLODGroup(LODGroup lodGroup, Vector3 probeSpaces) {
			var lods = lodGroup.GetLODs();
			for(var level = 1; level < lods.Length; ++level) {
				// TODO: Check if level is static and count level from first non-static
				// and treat levels as if incremented by 1, so level 0 has proxy spacing equal to probe spacing

				// TODO: Check for scale in lightmap (of level 0) and scale spacing accordingly

				foreach(var renderer in lods[level].renderers) {
					var meshRender = renderer as MeshRenderer;
					if (!meshRender) continue;
					
					meshRender.receiveGI = ReceiveGI.LightProbes;
					
					var proxy = meshRender.GetComponent<LightProbeProxyVolume>();
					var proxyUpdate = meshRender.GetComponent<LightProbeProxyUpdate>();

					// If renderer is large, configure it to use a proxy volume
					// NOTE: Renderer bounds are computed in world coordinates,
					// so for some objects, the majority of the proxy volume may be unused.
					// IDEA: This can be improved by partitioning the object.
					var levelSpaces = probeSpaces * Mathf.Pow(2, level - 1);
					var bounds = renderer.bounds;
					var useProxy = false;
					for(var i = 0; i < 3; ++i) useProxy |= bounds.size[i] > levelSpaces[i];
					if(!useProxy) {
						if(proxy) EP.Destroy(proxy);
						if(proxyUpdate) EP.Destroy(proxyUpdate);
						renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
						continue;
					}

					if(!proxy) proxy = EP.AddComponent<LightProbeProxyVolume>(renderer.gameObject);
					if(!proxyUpdate) proxyUpdate = EP.AddComponent<LightProbeProxyUpdate>(renderer.gameObject);

					// Configure proxy bounds
					proxy.boundingBoxMode = LightProbeProxyVolume.BoundingBoxMode.AutomaticWorld;

					// Configure spacing
					proxy.probePositionMode = LightProbeProxyVolume.ProbePositionMode.CellCorner;
					proxy.resolutionMode = LightProbeProxyVolume.ResolutionMode.Custom;
					proxy.gridResolutionX = ProxyResolution(bounds.size.x / levelSpaces.x);
					proxy.gridResolutionY = ProxyResolution(bounds.size.y / levelSpaces.y);
					proxy.gridResolutionZ = ProxyResolution(bounds.size.z / levelSpaces.z);

					// Remaining settings
					// TODO: Apply LightProbeProxyVolume.QualityMode.Normal to highest level of detail for dynamic objects
					proxy.qualityMode = LightProbeProxyVolume.QualityMode.Low;
					proxy.refreshMode = LightProbeProxyVolume.RefreshMode.ViaScripting;

					renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.UseProxyVolume;
					renderer.lightProbeProxyVolumeOverride = null;
				}
			}
		}
	}
}
