// Copyright 2021 Reification Incorporated
// Licensed under Apache 2.0. All Rights reserved.

using UnityEngine;

namespace Reification {
	/// <summary>
	/// Non-physical control for orbital movement
	/// </summary>
	public class SunOrbit : MonoBehaviour {
		public float spinStep = 1f; // degrees / second
		public float turnStep = 5f; // degrees

		public bool spinIncrease() {
			return
				Input.GetKeyDown(KeyCode.Equals) ||
				Input.GetKeyDown(KeyCode.Plus);
		}

		public bool spinDecrease() {
			return
				Input.GetKeyDown(KeyCode.Minus) ||
				Input.GetKeyDown(KeyCode.Underscore);
		}

		public bool spinStop() {
			return
				Input.GetKeyDown(KeyCode.Backspace);
		}

		public bool turnLeft() {
			return
				Input.GetKeyDown(KeyCode.LeftBracket) ||
				Input.GetKeyDown(KeyCode.LeftCurlyBracket);
		}

		public bool turnRight() {
			return
				Input.GetKeyDown(KeyCode.RightBracket) ||
				Input.GetKeyDown(KeyCode.RightCurlyBracket);
		}

		public bool turnCenter() {
			return
				Input.GetKeyDown(KeyCode.Backslash) ||
				Input.GetKeyDown(KeyCode.Pipe);
		}

		float spinRate = 0f;

		void Update() {
			if(spinIncrease()) spinRate += spinStep;
			if(spinDecrease()) spinRate -= spinStep;
			if(spinStop()) spinRate = 0f;

			if(turnLeft()) transform.rotation = Quaternion.Euler(0f, turnStep, 0f) * transform.rotation;
			if(turnRight()) transform.rotation = Quaternion.Euler(0f, -turnStep, 0f) * transform.rotation;
			if(turnCenter()) transform.rotation = Quaternion.identity;

			transform.rotation = transform.rotation * Quaternion.Euler(Time.deltaTime * spinRate, 0f, 0f);
		}
	}
}
