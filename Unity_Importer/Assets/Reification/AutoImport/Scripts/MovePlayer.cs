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

		/// <summary>
		/// Player maximum movement rate
		/// </summary>
		public float moveRate = 3f; // meters / second

		/// <summary>
		/// Name of input axis controlling forward / backward movement
		/// </summary>
		public string forwardAxisName = "Vertical";

		/// <summary>
		/// Name of input axis controlling right / left movement
		/// </summary>
		public string lateralAxisName = "Horizontal";

		void Start() {
			playerCamera = GetComponentInChildren<Camera>();
			controller = GetComponent<CharacterController>();
		}

		void Update() {
			// Get movement for frame
			var move2 = new Vector2(Input.GetAxis(lateralAxisName), Input.GetAxis(forwardAxisName));
			if(move2.magnitude > 1f) move2 /= move2.magnitude;

			// Align movement with camera
			var move3 = move2.x * playerCamera.transform.right + move2.y * playerCamera.transform.forward;
			var move3_SqrMagnitude = move3.sqrMagnitude;
			if(move3_SqrMagnitude > 0) move3 *= moveRate / Mathf.Sqrt(move3_SqrMagnitude);
			controller.SimpleMove(move3);
		}
	}
}
