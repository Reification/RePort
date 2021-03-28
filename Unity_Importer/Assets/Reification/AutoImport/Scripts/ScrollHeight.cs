using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Reification {
	/// <summary>
	/// Change local height using scroll wheel
	/// </summary>
	public class ScrollHeight: MonoBehaviour {
		public float scrollScale = 0.125f; // meters per scroll unit

		public float scrollModifierScale = 0.125f; // scroll speed / cursor speed

		public float maxHeight = 2.5f; // meters

		public float minHeight = 0.5f; // meters

		float iniHeight = 1.5f;

		CharacterController controller;

		// Start is called before the first frame update
		void Start() {
			iniHeight = transform.localPosition.y;
			controller = GetComponentInParent<CharacterController>();

			SetHeight(iniHeight);
		}

		// PROBLEM: Input.touchCount does not count Apple Trackpad touches
		// Apple Trackpad on macOS interprets 1 finger as mouse 0 (left),
		// 2 fingers as mouse 1 (right) but has no other options.
		// 2 fingers without pressing is a scroll.
		// SOLUTION: Single click emulation ( ctrl+click for right, option/alt+click for middle)
		// QUESTION: Does the button need to be held first?
		// ANSWER: Yes, modifier keys are not themselves mouse presses!
		// NOTE: The correct solution would be to import the InputLogic system

		bool MouseButtonIsPressed() {
			return
				Input.GetMouseButton(0) || // Primary
				Input.GetMouseButton(1) || // Secondary
				Input.GetMouseButton(2); // Scroll
		}

		bool ScrollModifierIsPressed() {
			return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
		}

		bool ScrollButtonIsPressed() {
			// Try direct input
			if(Input.GetMouseButton(2)) return true;

			// Try modified input
			if(MouseButtonIsPressed() && ScrollModifierIsPressed()) return true;

			return false;
		}

		bool scrollModifierIsPressed = false;
		Vector2 lastScroll = Vector2.zero;

		Vector2 ScrollDelta() {
			// Try direct input
			var scrollDelta = Input.mouseScrollDelta;
			if(scrollDelta.y != 0f) return scrollDelta;

			// Try modified input
			if(ScrollModifierIsPressed()) {
				if(!scrollModifierIsPressed) {
					scrollModifierIsPressed = true;
					lastScroll = Input.mousePosition;
					return Vector2.zero;
				}
				var nextScroll = (Vector2)Input.mousePosition - lastScroll;
				lastScroll = Input.mousePosition;
				return nextScroll * scrollModifierScale;
			} else {
				scrollModifierIsPressed = false;
			}

			return Vector2.zero;
		}

		// Update is called once per frame
		void Update() {

			if(ScrollButtonIsPressed()) SetHeight(iniHeight);
			else SetHeight(transform.localPosition.y + ScrollDelta().y * scrollScale);
		}

		void SetHeight(float height) {
			var newHeight = Mathf.Clamp(height, minHeight, maxHeight);

			// Extend player collider
			controller.height = newHeight + controller.radius;
			controller.center = new Vector3(0f, controller.height / 2f, 0f);

			// Raise player camera
			var newCameraPosition = transform.localPosition;
			newCameraPosition.y = newHeight;
			transform.localPosition = newCameraPosition;
		}
	}
}
