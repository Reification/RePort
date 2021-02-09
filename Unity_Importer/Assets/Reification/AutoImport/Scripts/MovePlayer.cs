// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using UnityEngine;

namespace Reification {
	/// <summary>
	/// Move the player relative to the direction of the camera 
	/// </summary>
	public class MovePlayer : MonoBehaviour {
		Camera playerCamera;
		CharacterController controller;

		public float moveRate = 3f; // meters / second

		bool UpPressed() {
			return Input.GetAxis("Vertical") > 0;
		}

		bool DownPressed() {
			return Input.GetAxis("Vertical") < 0;
		}

		bool LeftPressed() {
			return Input.GetAxis("Horizontal") < 0;
		}

		bool RightPressed() {
			return Input.GetAxis("Horizontal") > 0;
		}

		void Start() {
			playerCamera = GetComponentInChildren<Camera>();
			controller = GetComponent<CharacterController>();
		}

		void Update() {
			// Get movement for frame
			var move2 = Vector2.zero;
			if(UpPressed()) move2 += Vector2.up;
			if(DownPressed()) move2 += Vector2.down;
			if(LeftPressed()) move2 += Vector2.left;
			if(RightPressed()) move2 += Vector2.right;

			// Align movement with camera
			var move3 = move2.x * playerCamera.transform.right + move2.y * playerCamera.transform.forward;
			var move3_SqrMagnitude = move3.sqrMagnitude;
			if(move3_SqrMagnitude > 0) move3 *= moveRate / Mathf.Sqrt(move3_SqrMagnitude);
			controller.SimpleMove(move3);
		}
	}
}
