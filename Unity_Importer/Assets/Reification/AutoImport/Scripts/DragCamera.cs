// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using UnityEngine;

namespace Reification {
	/// <summary>
	/// Move the camera by dragging a point in view of the player
	/// </summary>
	public class DragCamera : MonoBehaviour {
		Camera playerCamera;

		private void Start() {
			playerCamera = GetComponent<Camera>();
		}

		bool MouseButtonIsPressed() {
			return
				Input.GetMouseButton(0) || // Left
				Input.GetMouseButton(1) || // Right
				Input.GetMouseButton(2); // Middle
		}

		bool firstDragFrame = true;

		Vector2 lastMousePosition; // Pixel coordinates

		// Input update and render update are synchronous and alternating
		private void Update() {
			if(!MouseButtonIsPressed()) {
				firstDragFrame = true;
				return;
			}

			var nextMousePosition = Input.mousePosition;

			// IMPORTANT: The first follow frame is used only to initialize lastLocalRay
			if(!firstDragFrame) FollowCursorInView(nextMousePosition, lastMousePosition);
			else firstDragFrame = false;

			lastMousePosition = nextMousePosition;
		}

		private Vector3 DirectionToScreenPoint(Vector3 direction) {
			var worldPosition = playerCamera.transform.position + direction;
			return playerCamera.WorldToScreenPoint(worldPosition);
		}

		// Cosine of angle from camera up to horizontal
		const float poleAngleEpsilon = 1e-2f;

		// Fraction of screen height near pole horizontal line
		const float poleHeightEpsilon = 2e-1f;

		// Fraction of pole height epsilon where horizontal motion is zero
		const float poleZeroFraction = 2e-1f;

		private void FollowCursorInView(Vector3 nextScreenPosition, Vector3 lastScreenPosition) {
			// Camera parent defines world basis
			var worldUp = playerCamera.transform.parent?.up ?? Vector3.up;

			// PROBLEM: When the dragged screen position matches the height of a pole,
			// horizontal dragging is not possible due to the requirement to keep the camera upright,
			// while adjacent to this line horizontal dragging results in opposite directions of rotation.
			// SOLUTION: Modify the next mouse position towards only vertical motion near this horizontal line.
			var poleHalfBand = Screen.height * poleHeightEpsilon;
			var upScreenPosition = DirectionToScreenPoint(worldUp);
			var upDelta = nextScreenPosition.y - upScreenPosition.y;
			if(
				upScreenPosition.z > 0f &&
				-poleHalfBand < upDelta && upDelta < poleHalfBand
			) {
				var bandFraction = Mathf.Abs(upDelta) / poleHalfBand;
				//Debug.Log("Crossing UP Pole Horizontal -> Clamp lastMousePosition, bandFraction = " + bandFraction);
				if(bandFraction < poleZeroFraction || 1f <= poleZeroFraction) nextScreenPosition.x = lastScreenPosition.x;
				else nextScreenPosition.x -= (nextScreenPosition.x - lastScreenPosition.x) * (1f - bandFraction) / (1f - poleZeroFraction);
			}
			var downScreenPosition = DirectionToScreenPoint(-worldUp);
			var downDelta = nextScreenPosition.y - downScreenPosition.y;
			if(
				downScreenPosition.z > 0f &&
				-poleHalfBand < downDelta && downDelta < poleHalfBand
			) {
				var bandFraction = Mathf.Abs(downDelta) / poleHalfBand;
				//Debug.Log("Crossing DOWN Pole Horizontal -> Clamp lastMousePosition, bandFraction = " + bandFraction);
				if(bandFraction < poleZeroFraction || 1f <= poleZeroFraction) nextScreenPosition.x = lastScreenPosition.x;
				else nextScreenPosition.x -= (nextScreenPosition.x - lastScreenPosition.x) * (1f - bandFraction) / (1f - poleZeroFraction);
			}

			// (0) Convert screen positions to local ray directions
			var nextLocalRay = playerCamera.transform.InverseTransformDirection(playerCamera.ScreenPointToRay(nextScreenPosition).direction);
			var lastLocalRay = playerCamera.transform.InverseTransformDirection(playerCamera.ScreenPointToRay(lastScreenPosition).direction);

			// (1) Minimal rotation that keeps cursor local ray invariant
			var nextRotation = playerCamera.transform.rotation * Quaternion.FromToRotation(nextLocalRay, lastLocalRay);

			// (2) Rotate around the mouse ray so that world up is in the forward-up plane
			// Computation will be with respect to local basis coordinates
			// (2.1) Rotation about the ray moves the world-up vector on a circle in a ray-perpendicular offset-plane
			// defined by points p satisfying: Dot(p, ray) == Dot(world-Up, ray)
			var cameraUp = Quaternion.Inverse(nextRotation) * worldUp;
			var cameraUpDotRay = Vector3.Dot(cameraUp, nextLocalRay);
			// (2.2) The solution requires a quadratic solve - existence can be checked now...
			var quadDenom = nextLocalRay.y * nextLocalRay.y + nextLocalRay.z * nextLocalRay.z;
			var sqrootArg = quadDenom - cameraUpDotRay * cameraUpDotRay;
			// NOTE: sqrootArg is ~0 when cursor has the same height as a pole in the camera view
			// (2.3) The intersection of the ray-perendicular offset-plane and the local-forward-up plane
			// defines a line that can be parameterized by local-y with a basepoint bLine and direction dLine
			var bLine = new Vector3(0f, 0f, cameraUpDotRay / nextLocalRay.z);
			var dLine = new Vector3(0, 1, -nextLocalRay.y / nextLocalRay.z);
			// (2.4) The two solution points are found by solving for the intersection of the line with unit sphere
			// of possible rotations of world-up with respect to the parameterizing coordinate y.
			var quadCenter = (cameraUpDotRay * nextLocalRay.y) / quadDenom;
			var quadSigned = nextLocalRay.z * Mathf.Sqrt(sqrootArg >= 0f ? sqrootArg : 0f) / quadDenom;
			var posPoint = bLine + (quadCenter + quadSigned) * dLine;
			var negPoint = bLine + (quadCenter - quadSigned) * dLine;
			// (2.5) To find the angle of rotation, project these vectors on to the ray-perpendicular plane
			var cameraUpProj = Vector3.ProjectOnPlane(cameraUp, nextLocalRay);
			var posPointProj = Vector3.ProjectOnPlane(posPoint, nextLocalRay);
			var posAngle = Vector3.SignedAngle(posPointProj, cameraUpProj, nextLocalRay);
			var negPointProj = Vector3.ProjectOnPlane(negPoint, nextLocalRay);
			var negAngle = Vector3.SignedAngle(negPointProj, cameraUpProj, nextLocalRay);
			// (2.6) Select the minimum angle to rotate and check that camera has not inverted
			bool usePos = Mathf.Abs(posAngle) <= Mathf.Abs(negAngle);
			var upRotation = Quaternion.AngleAxis(usePos ? posAngle : negAngle, nextLocalRay);
			playerCamera.transform.rotation = nextRotation * upRotation;

			// (3) Limit rotation range to keep camera upright
			float upAngle = Vector3.SignedAngle(playerCamera.transform.up, worldUp, playerCamera.transform.right);
			if(upAngle > 90f - poleAngleEpsilon) {
				var unflip = Quaternion.AngleAxis(upAngle - 90f + poleAngleEpsilon, playerCamera.transform.right);
				playerCamera.transform.rotation = unflip * playerCamera.transform.rotation;
			}
			if(upAngle < -90f + poleAngleEpsilon) {
				var unflip = Quaternion.AngleAxis(upAngle + 90f - poleAngleEpsilon, playerCamera.transform.right);
				playerCamera.transform.rotation = unflip * playerCamera.transform.rotation;
			}

			// (4) Rectify tilt
			// NOTE: This is only needed when sqrtArg < 0f, in which case there was no solution when pivoting on ray
			if(sqrootArg < 0f) {
				var tiltAngle = Vector3.Angle(worldUp, playerCamera.transform.right) - 90f;
				var tiltAxis = Vector3.Cross(worldUp, playerCamera.transform.right);
				//Debug.Log("sqrootArg = " + sqrootArg + " -> tiltAngle = " + tiltAngle);
				playerCamera.transform.rotation = Quaternion.AngleAxis(-tiltAngle, tiltAxis) * playerCamera.transform.rotation;
			}
		}
	}
}
