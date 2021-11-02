// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Reification {
	/// <summary>
	/// Prioritize updates of Light Probe Proxy Volumes
	/// </summary>
	/// <remarks>
	/// Independent of the proxy updates, the existence of each LightProbeProxyVolume
	/// incurs a cost in each frame via LightProbeProxyVolumeManager.Update().
	/// QUESTION: Is this cost based on instance or on proxy count?
	/// </remarks>
	public class LightProbeProxyUpdate: MonoBehaviour {
		LightProbeProxyVolume proxy;
		Renderer renderer;

		public static float targetPeriod = 1f / 15f; // Target frame duration TODO: This should be derived
		public static float adjustUpdate = 1000; // Proxy count adjustment relative to target frame period
		static int proxyLimit = 0; // Proxy update limit, rounded up by volume

		static int updateQueueFrame = -1;
		static List<LightProbeProxyUpdate> frameQueue = new List<LightProbeProxyUpdate>();
		static List<LightProbeProxyUpdate> buildQueue = new List<LightProbeProxyUpdate>();

		static void UpdateQueue(int proxyCount) {
			if(updateQueueFrame == Time.frameCount) return;
			updateQueueFrame = Time.frameCount;

			// Swap queues
			frameQueue = buildQueue;
			buildQueue = new List<LightProbeProxyUpdate>();

			// Sort queue
			frameQueue.Sort((l, r) =>
				l.queueScore < r.queueScore ? -1 :
				l.queueScore > r.queueScore ? 1 :
				0
			);

			// Determine update priority in queue
			var priorCount = 0;
			for(var index = 0; index < frameQueue.Count; ++index) {
				var update = frameQueue[index];
				update.priorCount = priorCount;
				priorCount += proxyCount;
			}

			// Adjust proxy limit
			// WARNING: In editor, rendered frame rate may be much less than profiled frame rate
			// NOTE: In editor, scene view will also yield OnWillRender calls.
			proxyLimit += Mathf.FloorToInt((targetPeriod - Time.deltaTime) * adjustUpdate);

			// Limit growth of proxy limit
			if(proxyLimit > priorCount) proxyLimit = priorCount;

			// Ensure at least one update occurs
			if(proxyLimit <= 0) proxyLimit = 1;
		}

		public static float intensityChange = 0.1f;  // Rendered intensity, in any channel
		public static float directionChange = 1.0f;  // Degrees, of directional light
		// TODO: Update for a point light should use direction to light, scaled by distance to light
		// NOTE: Lights can derive their own change assessments, including position change

		static int updateLightsFrame = -1;

		public class LightState {
			Light light;
			bool lastEnabled = false;
			Vector4 lastIntensity = Vector4.zero;
			Quaternion lastRotation = Quaternion.identity;

			public LightState(Light light) {
				this.light = light;
				Update();
			}

			public bool Update() {
				var change = false;
				var nextEnabled = light.isActiveAndEnabled && light.intensity > 0;
				change |= nextEnabled != lastEnabled;
				Vector4 nextIntensity = light.color * light.intensity;
				change |= (nextIntensity - lastIntensity).magnitude > intensityChange;
				var nextRotation = light.transform.rotation;
				change |= Vector3.Angle(lastRotation * Vector3.forward, nextRotation * Vector3.forward) > directionChange;
				if(change) {
					lastEnabled = nextEnabled;
					lastIntensity = nextIntensity;
					lastRotation = nextRotation;
					//Debug.Log($"Light {this.light.Path()} changed");
				}
				return change;
			}
		}

		public static List<LightState> lightStates = new List<LightState>();
		static bool lightsChanged = false;

		static void StartLights() {
			if(updateLightsFrame >= 0) return;

			var lightList = FindObjectsOfType<Light>();
			foreach(var light in lightList) {
				// NOTE: Mixed lights only modify direct lighting
				if(light.lightmapBakeType != LightmapBakeType.Realtime) continue;
				lightStates.Add(new LightState(light));
			}
		}

		static void UpdateLights() {
			StartLights();
			if(updateLightsFrame == Time.frameCount) return;
			updateLightsFrame = Time.frameCount;

			lightsChanged = false;
			foreach(var lightState in lightStates) lightsChanged |= lightState.Update();
		}

		int lastQueued = -1; // Last queued frame
		int lastUpdate = -1; // Last updated frame
		int lastChange = -1; // Last lighting change frame
		int priorCount = -1; // Count of all prior proxies in queue
		float queueScore = 0f; // Score of volume in queue

		int proxyCount = 0; // Count of probes in proxy volume

		// QUESTION: Is there an analogous update override for RTGI lightmaps? Is it needed?

		private void Start() {
			proxy = GetComponent<LightProbeProxyVolume>();
			renderer = GetComponent<Renderer>();
			if(!proxy || !renderer) {
				Debug.LogWarning($"LightProbeProxyUpdate requires LightProbeProxyVolume and Renderer components -> Destroy(this)");
				Destroy(this);
				return;
			}
			proxy.refreshMode = LightProbeProxyVolume.RefreshMode.ViaScripting;

			proxyCount = proxy.gridResolutionX * proxy.gridResolutionY * proxy.gridResolutionZ;

			UpdateLights();

			// Ensure that first frame has updates enqueued
			lastCameraScore = 1f;
			Enqueue();
		}

		private void Update() {
			UpdateLights();
			UpdateQueue(proxyCount);
		}

		// OnWillRenderObject is not called for culled objects.
		// OnWillRenderObject is not called for non-rendering LOD objects.
		private void OnWillRenderObject() {
			// IMPORTANT: Because priorCount does not include proxyCount, 
			// if proxyLimit > 0 then at least one proxy volume will update in every frame.
			if(0 <= priorCount && priorCount < proxyLimit) {
				//Debug.Log($"Proxy {this.gameObject.name} frame = {Time.frameCount} -> delta = {Time.frameCount - lastUpdate} && priorCount = {priorCount} < proxyLimit = {proxyLimit}");
				proxy.Update();
				lastUpdate = Time.frameCount;
			}
			// IMPORTANT: Update only once in each frame, even if multiple cameras are rendering,
			// and do not update unless enqueued
			priorCount = -1;

			// IDEA: In the case of an incomplete queue do not update
			// Instead, offset the priorCount range and skip the update

			UpdateCameraScore(Camera.current);
			Enqueue();
		}

		int lastCameraFrame = -1;
		float lastCameraScore = 0f;
		float nextCameraScore = 0f;

		void UpdateCameraScore(Camera camera) {
			// TODO: Use the LODGroup visible surface area offset to modify camera score
			// Camera score is proportionate to object pixels in camera view
			var focalPixel = camera.pixelHeight / Mathf.Tan(camera.fieldOfView / 2f); // Usually invariant
			var objectRadius = renderer.bounds.extents.sqrMagnitude; // Usually invariant
			var cameraRadius = (renderer.bounds.center - camera.transform.position).sqrMagnitude;
			var cameraScore = focalPixel * focalPixel * objectRadius / cameraRadius;

			// Queue priority depends on maximum camera score in previous frame
			if(lastCameraFrame < Time.frameCount) {
				lastCameraFrame = Time.frameCount;
				lastCameraScore = nextCameraScore;
				nextCameraScore = 0f;
			}
			if(nextCameraScore < cameraScore) nextCameraScore = cameraScore;
		}

		private void Enqueue() {
			// IMPORTANT: Enqueue only once in each frame, even if multiple cameras are rendering
			if(lastQueued == Time.frameCount) return;
			lastQueued = Time.frameCount;

			// TODO: In the case of non-static proxy volumes, also look for object change
			if(lightsChanged) lastChange = Time.frameCount;
			if(lastChange <= lastUpdate) return;

			// Add prioritized by pixels rendered in camera view and update latency
			var frameDelta = 1 + Time.frameCount - lastUpdate;
			queueScore = frameDelta * lastCameraScore;
			buildQueue.Add(this);
		}
	}
}
