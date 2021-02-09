// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using UnityEngine;

namespace Reification {
	public class SunColor : MonoBehaviour {
		public Color zenithColor = new Color(255f / 255f, 244f / 255f, 214f / 255f);
		public Color horizonColor = new Color(221f / 255f, 128f / 255f, 87f / 255f);
		public float intensity = 1f; // Intensity when visible
		public float gloaming = 10f; // Below-horizon non-zero intensity

		Light sunLight;

		void Start() {
			sunLight = GetComponent<Light>();
		}

		void Update() {
			var sunAngle = Vector3.Angle(Vector3.down, transform.forward);
			var sunInterpVal = Mathf.Clamp01(sunAngle / 90f);
			var sunIntensityScale = (1f - Mathf.Clamp01((sunAngle - 90f) / gloaming));
			var sunScale = sunIntensityScale * intensity;

			var sunColor = Color.Lerp(zenithColor, horizonColor, sunInterpVal);
			sunLight.intensity = sunScale;
			sunLight.color = sunColor;

			// Scale ambient intensity as to match sun intensity.
			RenderSettings.ambientIntensity = sunIntensityScale;

			// Reduce skybox reflection intensity to match sun intensity.
			RenderSettings.reflectionIntensity = sunIntensityScale;
		}
	}
}
