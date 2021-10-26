// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LightProbeProxyUpdate: MonoBehaviour {
	LightProbeProxyVolume proxy;

	static float targetPeriod = 1f / 20f; // Target frame duration TODO: This should be derived
	static float adjustUpdate = 10000; // Proxy count adjustment relative to target frame period

	static int proxyLimit = 0; // Proxy update limit, rounded up by volume
	static int buildFrame = -1;
	static List<LightProbeProxyUpdate> frameQueue = new List<LightProbeProxyUpdate>();
	static List<LightProbeProxyUpdate> buildQueue = new List<LightProbeProxyUpdate>();

	static void UpdateQueue(int proxyCount) {
		if(buildFrame == Time.frameCount) return;
		buildFrame = Time.frameCount;

		// Swap queues
		frameQueue = buildQueue;
		buildQueue = new List<LightProbeProxyUpdate>();

		// Sort queue
		frameQueue.Sort((l, r) =>
			l.queueScore < r.queueScore ? -1:
			l.queueScore > r.queueScore ? 1:
			0
		);

		// Determine update priority in queue
		var prior = 0;
		for(var index = 0; index < frameQueue.Count; ++index) {
			var update = frameQueue[index];
			update.priorCount = prior;
			prior += proxyCount;
		}

		// Adjust proxy limit
		proxyLimit += Mathf.FloorToInt((targetPeriod - Time.deltaTime) * adjustUpdate);
		Debug.Log($"Frame period = {Time.deltaTime} -> proxy limit = {proxyLimit}");
	}

	int lastQueued = -1; // Last queued frame
	int lastUpdate = -1; // Last updated frame
	int priorCount = -1; // Count of all prior proxies in queue
	float queueScore = 0f; // Score of volume in queue

	int proxyCount = 0; // Count of probes in proxy volume

	private void Start() {
		proxy = GetComponent<LightProbeProxyVolume>();
		if(!proxy) {
			Debug.LogWarning($"LightProbeProxyUpdate requires LightProbeProxyVolume component -> Destroy(this)");
			Destroy(this);
			return;
		}
		proxy.refreshMode = LightProbeProxyVolume.RefreshMode.ViaScripting;

		proxyCount = proxy.gridResolutionX * proxy.gridResolutionY * proxy.gridResolutionZ;

		Enqueue();
	}

	private void Update() {
		UpdateQueue(proxyCount);
	}

	private void OnWillRenderObject() {
		// NOTE: OnWillRenderObject is not called for non-rendering LOD.
		//Debug.Log($"Will render {gameObject.name}");

		// IMPORTANT: Because proxyPrior is used, if proxyLimit > 0 then
		// at least one proxy volume will update in every frame.
		if(0 <= priorCount && priorCount < proxyLimit) {
			proxy.Update();
			lastUpdate = Time.frameCount;
			priorCount = -1;
		}

		Enqueue();
	}

	private void Enqueue() {
		if(lastQueued == Time.frameCount) return;
		lastQueued = Time.frameCount;

		// TODO: Score is product of object radius * delta-time * viewing distance / center pixel angle
		// NOTE: If there are multiple cameras, use the minimum d / a.
		// NOTE: Ideal would be scale in view of player.

		// TEMP: Add randomly, and order by time
		var frameDelta = 1 + Time.frameCount - lastUpdate;
		queueScore = frameDelta;
		buildQueue.Add(this);

		// TODO: Each frame builds a priority queue for the next frame
		// and determines whether to update based on its position in the queue
		// constructed from the last frame. This requires a singleton manager,
		// which can be included with the PostProcessing and other Agent managers.
		// Objects that will not render will not be added to the next frame's queue
		// Object priority is based on frame count since last render, so the longer
		// the time since the last update, the higher the priority. When a scene is loaded
		// objects will not have been rendered previously.
		// NOTE: The queue may be only partially updated in a given frame, but it must be
		// fully recreated in each frame.

		// TODO: If there have been no changes in dynamic lighting (sun transform), no updates are required.
		// TODO: Lower levels of detail should have lower update priority. This could also be determined
		// by assessing distance from the renderer (Camera.current identifies renderer in this call).
		// TODO: Queue can be budgeted based on proxy resolution.

	}
}
