using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Reification {
	/// <summary>
	/// Change local height using scroll wheel
	/// </summary>
	public class ScrollHeight: MonoBehaviour {
		public float scrollScale = 0.125f; // meters per scroll unit

		public float maxHeight = 2.5f; // meters

		public float minHeight = 0.5f; // meters

		float iniHeight = 0f;

		// Start is called before the first frame update
		void Start() {
			iniHeight = transform.localPosition.y;
			SetHeight(iniHeight);
		}

		// Update is called once per frame
		void Update() {
			SetHeight(transform.localPosition.y + Input.mouseScrollDelta.y * scrollScale);

			if(Input.GetMouseButtonDown(2)) SetHeight(iniHeight);
		}

		void SetHeight(float height) {
			var localPosition = transform.localPosition;
			localPosition.y = Mathf.Clamp(height, minHeight, maxHeight);
			transform.localPosition = localPosition;
		}
	}
}
