// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LightProbeProxyUpdate: MonoBehaviour {
	LightProbeProxyVolume proxy;

	private void Start() {
		proxy = GetComponent<LightProbeProxyVolume>();
		if(!proxy) {
			Debug.LogWarning($"LightProbeProxyUpdate requires LightProbeProxyVolume component -> Destroy(this)");
			Destroy(this);
			return;
		}
		proxy.refreshMode = LightProbeProxyVolume.RefreshMode.ViaScripting;
	}

	private void OnWillRenderObject() {
		// NOTE: OnWillRenderObject is not called for non-rendering LOD.
		//Debug.Log($"Will render {gameObject.name}");
		proxy.Update();

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
